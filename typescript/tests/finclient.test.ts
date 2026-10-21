import { createHmac } from "node:crypto";
import { describe, expect, it, vi } from "vitest";
import { FinApiError, FinClient } from "../src/index.js";
import { FinWebhook } from "../src/webhook.js";

function json(data: unknown, status = 200, extra: Record<string, string> = {}): Response {
  return new Response(JSON.stringify({ data, code: status, isOk: status >= 200 && status < 300, message: null, errors: null, chksum: null }), {
    status,
    headers: { "Content-Type": "application/json", ...extra },
  });
}

describe("FinClient", () => {
  it("sends the API key and preserves PascalCase payload fields", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async (_input, init) => {
      const headers = new Headers(init?.headers);
      expect(headers.get("X-Api-Key")).toBe("example-key");
      return json([{ Fleet: { Account: { ACNO: 52014125, Login: "52014125" } }, State: { ACNO: 52014125, Snapshot: { Balance: 100.5 } } }]);
    });
    const client = new FinClient({ apiKey: "example-key", fetch });
    const accounts = await client.copy.accounts.list();

    expect(accounts.items[0]?.Fleet?.Account?.Login).toBe("52014125");
    expect(accounts.items[0]?.State?.ACNO).toBe(52014125);
    expect(accounts.items[0]?.State?.Snapshot?.Balance).toBe(100.5);
  });

  it("keeps the same Idempotency-Key and PascalCase body across a retry", async () => {
    const requests: RequestInit[] = [];
    const urls: string[] = [];
    const delays: number[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
      urls.push(String(input));
      requests.push(init ?? {});
      return requests.length === 1
        ? new Response("{}", { status: 503, headers: { "Retry-After": "2" } })
        : json({ queued: true });
    });
    const client = new FinClient({ apiKey: "example-key", fetch, retryDelay: async ms => { delays.push(ms); }, random: () => 0.5 });

    await client.copy.orders.make(52014125, { SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: 0.05 }, { idempotencyKey: "stable-write" });

    expect(requests).toHaveLength(2);
    expect(new Headers(requests[0]?.headers).get("Idempotency-Key")).toBe("stable-write");
    expect(new Headers(requests[1]?.headers).get("Idempotency-Key")).toBe("stable-write");
    expect(new URL(urls[1]!).pathname).toBe("/v1/fxs/accounts/52014125/trade");
    expect(JSON.parse(String(requests[1]?.body))).toEqual({ SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: 0.05 });
    expect(delays).toEqual([2000]);
  });

  it("adds a key to writes when the caller does not supply one", async () => {
    let idempotencyKey: string | null = null;
    const fetch = vi.fn<typeof globalThis.fetch>(async (_input, init) => {
      idempotencyKey = new Headers(init?.headers).get("Idempotency-Key");
      return json({ queued: true });
    });
    const client = new FinClient({ apiKey: "example-key", fetch });

    await client.copy.accounts.start(52014125);
    expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
  });

  it("maps API error envelopes to FinApiError", async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(JSON.stringify({
      data: null, code: 403, isOk: false, message: "Trade permission is required.", errors: ["Trade"], chksum: null,
    }), { status: 403 }));
    const client = new FinClient({ apiKey: "example-key", fetch, maxRetries: 0 });

    await expect(client.copy.accounts.list()).rejects.toMatchObject({
      name: "FinApiError", statusCode: 403, code: 403, message: "Trade permission is required.", errors: ["Trade"],
    });
    await expect(client.copy.accounts.list()).rejects.toBeInstanceOf(FinApiError);
  });

  it("retries 429 using Retry-After", async () => {
    let calls = 0;
    const delays: number[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async () => ++calls === 1
      ? new Response("{}", { status: 429, headers: { "Retry-After": "1" } })
      : json([]));
    const client = new FinClient({ apiKey: "example-key", fetch, retryDelay: async ms => { delays.push(ms); } });

    await client.copy.accounts.list();
    expect(calls).toBe(2);
    expect(delays).toEqual([1000]);
  });

  it("returns page rows and paging metadata", async () => {
    let requested = "";
    const fetch = vi.fn<typeof globalThis.fetch>(async input => {
      requested = String(input);
      return new Response(JSON.stringify({
        data: [{ PRNO: 9, SYM: "EURUSD" }], paging: { pageNo: 2, pageSize: 1, pageCount: 4, resCount: 4 },
        code: 200, isOk: true, message: null, errors: null, chksum: null,
      }), { status: 200 });
    });
    const client = new FinClient({ apiKey: "example-key", fetch });

    const page = await client.copy.accounts.pairs(52014125, { PageNo: 2, PageSize: 1, Search: "EUR USD" });
    expect(new URL(requested).searchParams.get("PageNo")).toBe("2");
    expect(new URL(requested).searchParams.get("search")).toBe("EUR USD");
    expect(page.items[0]?.PRNO).toBe(9);
    expect(page.paging?.resCount).toBe(4);
  });

  it("uses the member session with a caller-supplied fetch", async () => {
    let requestInit: RequestInit | undefined;
    const fetch = vi.fn<typeof globalThis.fetch>(async (_input, init) => { requestInit = init; return json({ State: { ACNO: 52014125 } }); });
    const client = new FinClient({ fetch });
    await client.copy.accounts.get(52014125);

    expect(new Headers(requestInit?.headers).has("X-Api-Key")).toBe(false);
    expect(requestInit?.credentials).toBe("include");
  });

  it("follows change-feed cursors until More is false", async () => {
    const urls: string[] = [];
    const fetch = vi.fn<typeof globalThis.fetch>(async input => {
      const url = String(input);
      urls.push(url);
      const cursor = new URL(url).searchParams.get("cursor");
      return cursor === null
        ? json({ Changes: [{ Entity: "Account", ACNO: 1 }], Cursor: "next/1", More: true })
        : json({ Changes: [{ Entity: "Order", OID: 22 }], Cursor: "next/2", More: false });
    });
    const client = new FinClient({ apiKey: "example-key", fetch });
    const changes = [];
    for await (const change of client.copy.changes.readAll()) changes.push(change);

    expect(changes.map(x => x.Entity)).toEqual(["Account", "Order"]);
    expect(new URL(urls[1]!).searchParams.get("cursor")).toBe("next/1");
  });
});

describe("FinWebhook", () => {
  it("accepts a valid signature and refuses a changed body", () => {
    const timestamp = "1792430400";
    vi.useFakeTimers();
    vi.setSystemTime(new Date(Number(timestamp) * 1000));
    const body = "{\"Order\":{\"OID\":42}}";
    const signature = createHmac("sha256", "webhook-secret").update(`${timestamp}.${body}`).digest("hex");
    try {
      expect(FinWebhook.verify("webhook-secret", timestamp, body, `sha256=${signature}`)).toBe(true);
      expect(FinWebhook.verify("webhook-secret", timestamp, `${body} `, signature)).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it("refuses a signature older than five minutes", () => {
    const now = 1_792_430_400;
    vi.useFakeTimers();
    vi.setSystemTime(new Date(now * 1000));
    const timestamp = String(now - 301);
    const body = "{}";
    const signature = createHmac("sha256", "webhook-secret").update(`${timestamp}.${body}`).digest("hex");
    try {
      expect(FinWebhook.verify("webhook-secret", timestamp, body, signature)).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });
});
