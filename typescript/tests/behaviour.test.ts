import { createHmac } from "node:crypto";
import { describe, expect, it, vi } from "vitest";
import { FinApiError, FinClient, FinWebhook } from "../src/index.js";

function json(data: unknown, status = 200, extra: Record<string, string> = {}): Response {
  return new Response(JSON.stringify({ data, code: status, isOk: status >= 200 && status < 300, message: null, errors: null, chksum: null }), {
    status,
    headers: { "Content-Type": "application/json", ...extra },
  });
}

function client(fetch: typeof globalThis.fetch, delays: number[] = [], extra: Partial<ConstructorParameters<typeof FinClient>[0]> = {}) {
  return new FinClient({ apiKey: "example-key", fetch, retryDelay: async ms => { delays.push(ms); }, random: () => 0.5, ...extra });
}

describe("retries and errors", () => {
  it("waits whole seconds on a 429 without Retry-After", async () => {
    let calls = 0;
    const delays: number[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async () => ++calls < 3 ? new Response("{}", { status: 429 }) : json([]));
    await client(fetch, delays).copy.accounts.list();
    expect(delays).toEqual([1000, 2000]);
  });

  it("stops and reports the wait when the server asks for more than a minute", async () => {
    const delays: number[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response("{}", { status: 429, headers: { "Retry-After": "120" } }));
    await expect(client(fetch, delays).copy.accounts.list()).rejects.toMatchObject({
      statusCode: 429, errorCode: "rate_limited", retryAfterMs: 120_000,
    });
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(delays).toEqual([]);
  });

  it("treats an empty Retry-After as not given", async () => {
    let calls = 0;
    const delays: number[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async () => ++calls === 1
      ? new Response("{}", { status: 503, headers: { "Retry-After": "" } })
      : json([]));
    await client(fetch, delays).copy.accounts.list();
    expect(delays).toEqual([250]);
  });

  it("wraps a network failure after the last retry", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => { throw new TypeError("fetch failed"); });
    const error = await client(fetch, [], { maxRetries: 1 }).copy.accounts.list().catch(e => e);
    expect(error).toBeInstanceOf(FinApiError);
    expect(error).toMatchObject({ statusCode: 0, errorCode: "network" });
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("wraps a timeout after the last retry", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>((_input, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener("abort", () => reject(init.signal?.reason));
    }));
    const error = await client(fetch, [], { maxRetries: 0, timeoutMs: 5 }).copy.accounts.list().catch(e => e);
    expect(error).toMatchObject({ statusCode: 0, errorCode: "timeout" });
  });

  it("does not wrap the caller's own cancellation", async () => {
    const controller = new AbortController();
    const fetch = vi.fn<typeof globalThis.fetch>((_input, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener("abort", () => reject(init.signal?.reason));
      controller.abort(new Error("stopped by caller"));
    }));
    await expect(client(fetch).copy.accounts.list({}, undefined, { signal: controller.signal })).rejects.toThrow("stopped by caller");
  });

  it("maps a gateway HTML page to FinApiError", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response("<html>502 Bad Gateway</html>", { status: 502 }));
    await expect(client(fetch, [], { maxRetries: 0 }).copy.accounts.list()).rejects.toMatchObject({ statusCode: 502, errorCode: null });
  });

  it("reports a successful reply without data", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(JSON.stringify({ code: 200, isOk: true }), { status: 200 }));
    await expect(client(fetch).copy.accounts.list()).rejects.toMatchObject({ errorCode: "empty_response" });
  });
});

describe("requests", () => {
  it("leaves unset fields out of the body", async () => {
    let body = "";
    const fetch = vi.fn<typeof globalThis.fetch>(async (_input, init) => { body = String(init?.body); return json(true); });
    await client(fetch).copy.orders.make(52014125, { SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: null } as never);
    expect(JSON.parse(body)).toEqual({ SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY" });
  });

  it("refuses an idempotency key the server would refuse", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => json(true));
    await expect(client(fetch).copy.accounts.start(52014125, { idempotencyKey: "x".repeat(121) })).rejects.toBeInstanceOf(TypeError);
    await expect(client(fetch).copy.accounts.start(52014125, { idempotencyKey: "bad\nkey" })).rejects.toBeInstanceOf(TypeError);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("passes an enum value it does not know through unchanged", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => json([{ State: { ACNO: 1, Status: "SOMETHING_NEW" } }]));
    const accounts = await client(fetch).copy.accounts.list();
    expect((accounts.items[0]?.State as Record<string, unknown>)["Status"]).toBe("SOMETHING_NEW");
  });
});

describe("change feed pages", () => {
  it("yields each page with its cursor", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async input => new URL(String(input)).searchParams.get("cursor") === null
      ? json({ Changes: [{ Entity: "Account" }], Cursor: "c1", More: true })
      : json({ Changes: [{ Entity: "Order" }], Cursor: "c2", More: false }));
    const cursors: (string | null)[] = [];
    for await (const page of client(fetch).copy.changes.readPages()) cursors.push(page.Cursor);
    expect(cursors).toEqual(["c1", "c2"]);
  });
});

describe("FinWebhook edges", () => {
  const now = 1_792_430_400;
  const sign = (timestamp: string, body: string) => createHmac("sha256", "s").update(`${timestamp}.${body}`).digest("hex");

  it("refuses a timestamp more than five minutes ahead", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(now * 1000));
    try {
      const timestamp = String(now + 301);
      expect(FinWebhook.verify("s", timestamp, "{}", sign(timestamp, "{}"))).toBe(false);
    } finally { vi.useRealTimers(); }
  });

  it("refuses a malformed or short signature", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(now * 1000));
    try {
      expect(FinWebhook.verify("s", String(now), "{}", "sha256=abc")).toBe(false);
      expect(FinWebhook.verify("s", String(now), "{}", "not-hex".repeat(10))).toBe(false);
    } finally { vi.useRealTimers(); }
  });

  it("verifies a byte body", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(now * 1000));
    try {
      const timestamp = String(now);
      expect(FinWebhook.verify("s", timestamp, new TextEncoder().encode("{}"), sign(timestamp, "{}"))).toBe(true);
    } finally { vi.useRealTimers(); }
  });
});
