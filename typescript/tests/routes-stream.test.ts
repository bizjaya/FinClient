import { describe, expect, it, vi } from "vitest";
import { FinApiError, FinClient } from "../src/index.js";
import { FinSseParser } from "../src/sse.js";

function json(data: unknown): Response {
  return new Response(JSON.stringify({ data, code: 200, isOk: true, message: null, errors: null, chksum: null }), {
    status: 200, headers: { "Content-Type": "application/json" },
  });
}

function recorder(reply: (url: URL, init?: RequestInit) => Response = () => json({})) {
  const calls: { method: string; url: URL; headers: Headers; body: unknown }[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    const url = new URL(String(input));
    calls.push({ method: init?.method ?? "GET", url, headers: new Headers(init?.headers), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    return reply(url, init);
  });
  return { calls, client: new FinClient({ apiKey: "example-key", fetch }) };
}

function eventStream(...chunks: (string | Error)[]): Response {
  const encoder = new TextEncoder();
  let index = 0;
  const body = new ReadableStream<Uint8Array>({
    pull(controller) {
      const next = chunks[index++];
      if (next === undefined) controller.close();
      else if (next instanceof Error) controller.error(next);
      else controller.enqueue(encoder.encode(next));
    },
  });
  return new Response(body, { status: 200, headers: { "Content-Type": "text/event-stream" } });
}

describe("server-sent events", () => {
  it("skips comments, joins data lines and names unnamed events", () => {
    const parser = new FinSseParser();
    const events = [": open", "retry: 5000", "event: orders", "data: {}", "", "data: one", "data:two", "", "event: empty", ""]
      .map((line) => parser.add(line)).filter((item) => item !== null);
    expect(events).toEqual([{ event: "orders", data: "{}" }, { event: "message", data: "one\ntwo" }]);
  });

  it("delivers an event left open when the stream ends", () => {
    const parser = new FinSseParser();
    parser.add("event: pairs");
    parser.add("data: {}");
    expect(parser.complete()).toEqual({ event: "pairs", data: "{}" });
    expect(new FinSseParser().complete()).toBeNull();
  });

  it("yields events split across chunks and sends the key", async () => {
    const { calls, client } = recorder(() => eventStream(": open\n\nevent: ord", "ers\ndata: {}\n\r\nevent: pairs\ndata: {}\n\n"));
    const names: string[] = [];
    for await (const item of client.copy.fx.stream()) names.push(item.event);
    expect(names).toEqual(["orders", "pairs"]);
    expect(calls[0]?.url.pathname).toBe("/v1/fxs/stream");
    expect(calls[0]?.headers.get("X-Api-Key")).toBe("example-key");
  });

  it("reports a stream that breaks midway as a network error", async () => {
    const { client } = recorder(() => eventStream("event: orders\ndata: {}\n\n", new Error("socket hang up")));
    const names: string[] = [];
    const run = async () => { for await (const item of client.copy.fx.stream()) names.push(item.event); };
    await expect(run()).rejects.toMatchObject({ errorCode: "network", statusCode: 0 });
    expect(names).toEqual(["orders"]);
  });

  it("reports a stream that cannot open as a network error", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => { throw new TypeError("fetch failed"); });
    const client = new FinClient({ apiKey: "example-key", fetch });
    const run = async () => { for await (const _ of client.copy.fx.stream()) { /* none */ } };
    await expect(run()).rejects.toBeInstanceOf(FinApiError);
  });
});

describe("routes", () => {
  it("carries the filters of cross-account lists", async () => {
    const { calls, client } = recorder(() => json([]));
    await client.copy.fx.pairs({ acno: 1234567890, demo: true, page: { PageNo: 2 } });
    await client.copy.fx.operations({ PageSize: 5 }, 1234567890);
    await client.copy.fx.dca(1234567890);
    expect(calls.map((call) => `${call.url.pathname}${call.url.search}`)).toEqual([
      "/v1/fxs/pairs?PageNo=2&acno=1234567890&demo=true",
      "/v1/fxs/operations?PageSize=5&acno=1234567890",
      "/v1/fxs/dca?acno=1234567890",
    ]);
  });

  it("reads cross-account performance unless narrowed", async () => {
    const { calls, client } = recorder();
    await client.copy.fx.performanceWindow();
    await client.copy.fx.performanceBars({ acno: 1234567890, demo: false, fromDt: new Date("2026-10-01T00:00:00Z") });
    expect(calls[0]?.url.pathname).toBe("/v1/fxs/performance/window");
    expect(calls[0]?.url.search).toBe("");
    expect(calls[1]?.url.searchParams.get("acno")).toBe("1234567890");
    expect(calls[1]?.url.searchParams.get("demo")).toBe("false");
    expect(calls[1]?.url.searchParams.get("fromDt")).toBe("2026-10-01T00:00:00.000Z");
  });

  it("sends hedge, modify, cancel and DCA answers with an idempotency key", async () => {
    const { calls, client } = recorder();
    await client.copy.orders.hedge(1234567890, { SYM: "EURUSD" });
    await client.copy.orders.modify(1234567890, { Ticket: 5 });
    await client.copy.orders.cancelWaiting(1234567890, { Oid: 6 });
    await client.copy.orders.answerDca(1234567890, 7, { Accept: true });
    const base = "/v1/fxs/accounts/1234567890";
    expect(calls.map((call) => `${call.method} ${call.url.pathname}`)).toEqual([
      `POST ${base}/trade/hedge`, `POST ${base}/trade/modify`, `POST ${base}/trade/cancel`, `POST ${base}/dca/7`,
    ]);
    for (const call of calls) expect(call.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/i);
    expect(calls[3]?.body).toEqual({ Accept: true });
  });

  it("removes an account with DELETE and the caller's key", async () => {
    const { calls, client } = recorder(() => json({ OperationId: "op-1" }));
    const queued = await client.copy.accounts.remove(1234567890, { idempotencyKey: "remove-once" });
    expect(queued.OperationId).toBe("op-1");
    expect(calls[0]?.method).toBe("DELETE");
    expect(calls[0]?.url.pathname).toBe("/v1/fxs/accounts/1234567890");
    expect(calls[0]?.headers.get("Idempotency-Key")).toBe("remove-once");
  });

  it("previews, applies and edits strategies", async () => {
    const { calls, client } = recorder((url, init) => json((init?.method ?? "GET") === "GET" ? [] : {}));
    await client.copy.accounts.previewStrategy(1234567890, { ISNO: 42 });
    await client.copy.accounts.applyStrategy(1234567890, { ISNO: 42 });
    await client.copy.fx.makeStrategy({ StratName: "Mine" });
    await client.copy.fx.patchStrategy(42, { StratName: "Renamed" });
    await client.copy.fx.deleteStrategy(42);
    await client.copy.fx.strategies(1234567890);
    expect(calls.map((call) => `${call.method} ${call.url.pathname}${call.url.search}`)).toEqual([
      "POST /v1/fxs/accounts/1234567890/strategy/preview",
      "POST /v1/fxs/accounts/1234567890/strategy/apply",
      "POST /v1/fxs/strategies",
      "PATCH /v1/fxs/strategies/42",
      "DELETE /v1/fxs/strategies/42",
      "GET /v1/fxs/strategies?acno=1234567890",
    ]);
    expect(calls[1]?.body).toEqual({ ISNO: 42 });
  });
});
