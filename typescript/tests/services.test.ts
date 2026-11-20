import { describe, expect, it, vi } from "vitest";
import { FinApiError, FinClient } from "../src/index.js";

interface Seen { url: URL; init: RequestInit; headers: Headers }

/** A fetch that answers from a queue (the last answer repeats) and records what it was sent. */
function recorder(...answers: Array<Response | (() => Response)>) {
  const seen: Seen[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    seen.push({ url: new URL(String(input)), init: init ?? {}, headers: new Headers(init?.headers) });
    const next = answers[Math.min(seen.length - 1, answers.length - 1)]!;
    return typeof next === "function" ? next() : next.clone();
  });
  return { seen, fetch };
}

function ok(body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json", ...headers } });
}

function refused(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

const noWait = async () => undefined;

/** The error a call rejected with; fails the test when the call succeeds. */
async function rejection(call: Promise<unknown>): Promise<FinApiError> {
  try { await call; } catch (error) { return error as FinApiError; }
  throw new Error("The call was expected to reject.");
}

function clientFor(fetch: typeof globalThis.fetch) {
  return new FinClient({
    graph: { apiKey: "graph-key" }, news: { apiKey: "news-key" }, social: { apiKey: "social-key" },
    fetch, retryDelay: noWait, random: () => 0.5,
  });
}

describe("service keys and addresses", () => {
  it("sends each service's key in FinUsrKey to its own default address, and never X-Api-Key", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.assets.types();
    await fin.news.categories.flat();
    await fin.social.videos.list();

    expect(seen.map((s) => s.url.origin)).toEqual(["https://x.fingraph.io", "https://x.finnews.io", "https://x.finsocial.io"]);
    expect(seen.map((s) => s.headers.get("FinUsrKey"))).toEqual(["graph-key", "news-key", "social-key"]);
    for (const s of seen) expect(s.headers.get("X-Api-Key")).toBeNull();
  });

  it("uses a service block's own baseUrl and keeps FinCopy on X-Api-Key", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = new FinClient({
      apiKey: "copy-key", graph: { apiKey: "g", baseUrl: "https://graph.example.test/" }, fetch, retryDelay: noWait,
    });

    await fin.graph.assets.types();
    await fin.copy.accounts.list();

    expect(seen[0]!.url.href).toBe("https://graph.example.test/api/assets/types");
    expect(seen[0]!.headers.get("FinUsrKey")).toBe("g");
    expect(seen[1]!.url.origin).toBe("https://api.fincopy.io");
    expect(seen[1]!.headers.get("X-Api-Key")).toBe("copy-key");
    expect(seen[1]!.headers.get("FinUsrKey")).toBeNull();
  });

  it("builds without FinCopy's key when a service block is given, and fails only when FinCopy is first used", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    // With no custom fetch, FinCopy has nothing to authenticate with.
    vi.stubGlobal("fetch", fetch);
    try {
      const fin = new FinClient({ graph: { apiKey: "g" } });

      await expect(fin.graph.assets.types()).resolves.toBeDefined();
      await expect(fin.copy.accounts.list()).rejects.toThrow(/FinCopy needs an API key/);
      expect(seen).toHaveLength(1);
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("still refuses a client with no key and no service block", () => {
    expect(() => new FinClient({})).toThrow(/apiKey is required/);
  });

  it("throws a clear error at first use of a part whose key is missing, without sending anything", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = new FinClient({ apiKey: "copy-key", news: { apiKey: "n" }, fetch });

    await expect(fin.graph.assets.types()).rejects.toThrow(/FinGraph needs an API key.*graph: \{ apiKey \}/);
    await expect(fin.social.markets.pulseList()).rejects.toThrow(/FinSocial needs an API key/);
    expect(seen).toHaveLength(0);
    await expect(fin.news.categories.flat()).resolves.toBeDefined();
  });

  it("applies the shared timeout and retry settings to every service", async () => {
    let calls = 0;
    const { fetch } = recorder(() => (++calls < 3 ? new Response("{}", { status: 503 }) : ok({ data: [], isOk: true })));
    const fin = new FinClient({ news: { apiKey: "n" }, fetch, retryDelay: noWait, maxRetries: 2, random: () => 0.5 });

    await fin.news.categories.flat();

    expect(calls).toBe(3);
  });
});

describe("the two surfaces", () => {
  it("sends /api operations from the groups and /v1 operations from v1", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.tickers.list();
    await fin.graph.v1.tickers.list();
    await fin.news.sources.categories();
    await fin.news.v1.sources.categories();
    await fin.social.markets.pulseList();
    await fin.social.v1.markets.pulseList();

    expect(seen.map((s) => s.url.pathname)).toEqual([
      "/api/tickers", "/v1/tickers", "/api/sources/categories", "/v1/sources/categories", "/api/markets/pulse", "/v1/markets/pulse",
    ]);
    expect(seen.every((s) => s.init.method === "GET")).toBe(true);
  });

  it("puts path arguments in the path, encoded", async () => {
    const { seen, fetch } = recorder(ok({ data: {}, code: 200, isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.tickers.view("BTC/USDT");
    await fin.graph.v1.assets.get("a b");
    await fin.news.news.view("abc123");
    await fin.social.markets.historyList("m-1", { Limit: 5 });

    expect(seen.map((s) => s.url.pathname + s.url.search)).toEqual([
      "/api/tickers/BTC%2FUSDT", "/v1/assets/a%20b", "/api/news/abc123", "/api/markets/history/m-1?Limit=5",
    ]);
  });

  it("rejects an empty path argument instead of calling a different route", async () => {
    const { seen, fetch } = recorder(ok({ data: {}, isOk: true }));
    const fin = clientFor(fetch);

    await expect(fin.graph.tickers.view("")).rejects.toThrow(RangeError);
    expect(seen).toHaveLength(0);
  });

  it("sends named query fields as they are named in the document, leaving out unset ones and formatting dates", async () => {
    const { seen, fetch } = recorder(ok({ data: [], isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.assets.list({ dax: "BIN", canTrade: false, limit: 25, quote: undefined });
    await fin.graph.ohlcv.view("BTCUSDT", { timeframe: "1d", from: "2026-11-01T00:00:00Z", limit: 10 });
    await fin.social.markets.historyList("m-1", { From: new Date("2026-11-01T08:00:00+08:00") });
    await fin.graph.sigDax.topGain({ dax: "BIN" });

    expect(seen[0]!.url.search).toBe("?dax=BIN&canTrade=false&limit=25");
    expect(seen[1]!.url.search).toBe("?timeframe=1d&from=2026-11-01T00%3A00%3A00Z&limit=10");
    expect(seen[2]!.url.search).toBe("?From=2026-11-01T00%3A00%3A00.000Z");
    expect(seen[3]!.url.searchParams.get("dax")).toBe("BIN");
  });

  it("encodes the qry object as one JSON value and leaves out nulls", async () => {
    const { seen, fetch } = recorder(ok({ data: [], paging: { pageNo: 1, pageCount: 1 }, isOk: true }));
    const fin = clientFor(fetch);

    await fin.news.news.list({ pageNo: 2, pageSize: 10, sortBy: [["published", "desc"]], Symbols: "AAPL,MSFT", Search: undefined });
    await fin.social.v1.social.postList({ Ticker: "BTC", Platform: null as never });
    await fin.news.v1.news.all();

    expect(seen[0]!.url.pathname).toBe("/api/news");
    expect([...seen[0]!.url.searchParams.keys()]).toEqual(["qry"]);
    expect(JSON.parse(seen[0]!.url.searchParams.get("qry")!)).toEqual({
      pageNo: 2, pageSize: 10, sortBy: [["published", "desc"]], Symbols: "AAPL,MSFT",
    });
    expect(JSON.parse(seen[1]!.url.searchParams.get("qry")!)).toEqual({ Ticker: "BTC" });
    expect(seen[2]!.url.pathname).toBe("/v1/news/all");
    expect(seen[2]!.url.searchParams.get("qry")).toBe("{}");
  });

  it("sends a JSON body with an idempotency key for a write, and reuses the key on a retry", async () => {
    const { seen, fetch } = recorder(new Response("{}", { status: 503 }), ok({ data: { Title: "x" }, code: 200, isOk: true }));
    const fin = clientFor(fetch);

    const reply = await fin.news.news.ratingMake("abc123", { ReqSite: "finquest", ReqUsno: 77 }, { idempotencyKey: "rate-1" });

    expect(reply.data).toEqual({ Title: "x" });
    expect(seen).toHaveLength(2);
    for (const s of seen) {
      expect(s.url.pathname).toBe("/api/news/abc123/rating");
      expect(s.init.method).toBe("POST");
      expect(s.headers.get("Content-Type")).toBe("application/json");
      expect(s.headers.get("Idempotency-Key")).toBe("rate-1");
      expect(s.headers.get("FinUsrKey")).toBe("news-key");
      expect(JSON.parse(String(s.init.body))).toEqual({ ReqSite: "finquest", ReqUsno: 77 });
    }
  });

  it("makes an idempotency key for a write when none is given, and none for a read", async () => {
    const { seen, fetch } = recorder(ok({ data: {}, isOk: true }));
    const fin = clientFor(fetch);

    await fin.news.videos.ratingMake("v1", {});
    await fin.news.videos.view("v1");

    expect(seen[0]!.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(seen[1]!.headers.get("Idempotency-Key")).toBeNull();
  });

  it("returns the whole documented reply, not just its data", async () => {
    const body = { data: [{ Id: 1 }], paging: { pageNo: 1, pageSize: 10, pageCount: 1, resCount: 1 }, code: 200, isOk: true, errors: [] };
    const { fetch } = recorder(ok(body));
    const fin = clientFor(fetch);

    const reply = await fin.news.news.list();

    expect(reply).toEqual(body);
  });

  it("returns a /v1 reply as it is", async () => {
    const body = { offset: 0, limit: 100, data: [{ Id: "a" }], count: 1 };
    const { fetch } = recorder(ok(body));
    const fin = clientFor(fetch);

    expect(await fin.graph.v1.assets.list()).toEqual(body);
  });
});

describe("refusals", () => {
  async function fail(status: number, body: unknown, headers: Record<string, string> = {}): Promise<FinApiError> {
    const { fetch } = recorder(refused(status, body, headers));
    const fin = new FinClient({ graph: { apiKey: "g" }, fetch, retryDelay: noWait, maxRetries: 0 });
    return rejection(fin.graph.tickers.list());
  }

  it("maps 401 with the API's error code and message", async () => {
    const error = await fail(401, { error: "invalid_api_key", message: "The key is unknown." });
    expect(error).toBeInstanceOf(FinApiError);
    expect(error.statusCode).toBe(401);
    expect(error.error).toBe("invalid_api_key");
    expect(error.message).toBe("The key is unknown.");
    expect(error.errorCode).toBeNull();
  });

  it("maps 402 with the limit details", async () => {
    const error = await fail(402, { error: "insufficient_credits", message: "Not enough credits.", lane: "CreditBudget", limit: 100 });
    expect(error.statusCode).toBe(402);
    expect(error.error).toBe("insufficient_credits");
    expect(error.lane).toBe("CreditBudget");
    expect(error.limit).toBe(100);
    expect(error.catag).toBeNull();
    expect(error.tier).toBeNull();
  });

  it("maps 403 with the category and tier", async () => {
    const error = await fail(403, { error: "package_not_allowed", message: "Upgrade.", catag: "SIGDAX", tier: "1", lane: "Tier", limit: "2" });
    expect(error.statusCode).toBe(403);
    expect(error.error).toBe("package_not_allowed");
    expect(error.catag).toBe("SIGDAX");
    expect(error.tier).toBe("1");
    expect(error.limit).toBe("2");
  });

  it("maps 429 and reports Retry-After when it is too long to wait out", async () => {
    const error = await fail(429, { error: "rate_limited", message: "Slow down." }, { "Retry-After": "120" });
    expect(error.statusCode).toBe(429);
    expect(error.errorCode).toBe("rate_limited");
    expect(error.retryAfterMs).toBe(120_000);
    expect(error.error).toBe("rate_limited");
  });

  it("retries 429 after the Retry-After the server gave, then succeeds", async () => {
    const delays: number[] = [];
    const { seen, fetch } = recorder(
      refused(429, { error: "rate_limited", message: "Slow down." }, { "Retry-After": "3" }),
      ok({ data: [], isOk: true }),
    );
    const fin = new FinClient({ graph: { apiKey: "g" }, fetch, retryDelay: async (ms) => { delays.push(ms); } });

    await fin.graph.tickers.list();

    expect(seen).toHaveLength(2);
    expect(delays).toEqual([3000]);
  });

  it("maps a /v1 error body", async () => {
    const error = await fail(501, { error: { code: "not_implemented", message: "Not available yet." } });
    expect(error.statusCode).toBe(501);
    expect(error.error).toBe("not_implemented");
    expect(error.message).toBe("Not available yet.");
  });

  it("maps an /api envelope that says it failed on HTTP 200", async () => {
    const { fetch } = recorder(ok({ isOk: false, code: 404, message: "No such ticker.", errors: ["symbol"] }));
    const fin = new FinClient({ graph: { apiKey: "g" }, fetch });

    const error = await rejection(fin.graph.tickers.view("NOPE"));

    expect(error).toBeInstanceOf(FinApiError);
    expect(error.message).toBe("No such ticker.");
    expect(error.code).toBe(404);
    expect(error.errors).toEqual(["symbol"]);
  });

  it("reports an unreadable error body with a plain message", async () => {
    const { fetch } = recorder(new Response("<html>bad gateway</html>", { status: 500 }));
    const fin = new FinClient({ graph: { apiKey: "g" }, fetch, maxRetries: 0 });

    const error = await rejection(fin.graph.tickers.list());

    expect(error.statusCode).toBe(500);
    expect(error.message).toBe("The FinGraph API request failed with HTTP 500.");
  });

  it("reports a network failure and a malformed 200 for a named service", async () => {
    const down = new FinClient({ news: { apiKey: "n" }, fetch: async () => { throw new TypeError("offline"); }, retryDelay: noWait, maxRetries: 0 });
    const network = await rejection(down.news.categories.flat());
    expect(network.errorCode).toBe("network");
    expect(network.message).toBe("The FinNews API could not be reached.");

    const { fetch } = recorder(new Response("not json", { status: 200 }));
    const garbled = new FinClient({ news: { apiKey: "n" }, fetch });
    const invalid = await rejection(garbled.news.categories.flat());
    expect(invalid.errorCode).toBe("invalid_response");
  });

  it("does not wrap the caller's own cancellation", async () => {
    const controller = new AbortController();
    controller.abort(new Error("stop"));
    const { fetch } = recorder(ok({ data: [], isOk: true }));
    const fin = clientFor(fetch);

    await expect(fin.graph.tickers.list({}, { signal: controller.signal })).rejects.toThrow("stop");
  });
});

describe("paging", () => {
  const row = (n: number) => ({ Id: n });
  const paged = (rows: number[], pageNo: number, pageCount: number) =>
    ok({ data: rows.map(row), paging: { pageNo, pageSize: 2, pageCount, resCount: 5 }, code: 200, isOk: true });

  it("walks a page-numbered list page by page, advancing pageNo, and stops after the last page", async () => {
    const { seen, fetch } = recorder(paged([1, 2], 1, 3), paged([3, 4], 2, 3), paged([5], 3, 3));
    const fin = clientFor(fetch);

    const pages: number[] = [];
    for await (const page of fin.news.news.listPages({ pageSize: 2, Symbols: "AAPL" })) pages.push(page.data?.length ?? 0);

    expect(pages).toEqual([2, 2, 1]);
    expect(seen.map((s) => JSON.parse(s.url.searchParams.get("qry")!).pageNo)).toEqual([1, 2, 3]);
    expect(JSON.parse(seen[2]!.url.searchParams.get("qry")!)).toEqual({ pageNo: 3, pageSize: 2, Symbols: "AAPL" });
  });

  it("walks every item, starting from the page the query names", async () => {
    const { seen, fetch } = recorder(paged([3, 4], 2, 3), paged([5], 3, 3));
    const fin = clientFor(fetch);

    const ids: number[] = [];
    for await (const item of fin.news.news.listItems({ pageNo: 2, pageSize: 2 })) ids.push((item as unknown as { Id: number }).Id);

    expect(ids).toEqual([3, 4, 5]);
    expect(seen).toHaveLength(2);
  });

  it("stops at an empty page when the paging block does not say it is the last, and does not yield it", async () => {
    const { seen, fetch } = recorder(
      ok({ data: [row(1)], paging: { pageNo: 1 }, isOk: true }),
      ok({ data: [], paging: { pageNo: 2 }, isOk: true }),
    );
    const fin = clientFor(fetch);

    const pages = [];
    for await (const page of fin.social.markets.marketListPages()) pages.push(page);

    expect(pages).toHaveLength(1);
    expect(seen).toHaveLength(2);
  });

  it("yields nothing for an empty list and does not change the query it was given", async () => {
    const { seen, fetch } = recorder(ok({ data: [], paging: { pageNo: 1, pageCount: 0 }, isOk: true }));
    const fin = clientFor(fetch);
    const query = { pageSize: 5 };

    const items = [];
    for await (const item of fin.news.sources.listItems(query)) items.push(item);

    expect(items).toEqual([]);
    expect(seen).toHaveLength(1);
    expect(query).toEqual({ pageSize: 5 });
  });

  it("advances offset on FinGraph's limit and offset lists and stops at an empty page", async () => {
    const list = (rows: number[]) => ok({ data: rows.map(row), code: 200, isOk: true });
    const { seen, fetch } = recorder(list([1, 2]), list([3]), list([]));
    const fin = clientFor(fetch);

    const ids: number[] = [];
    for await (const item of fin.graph.assets.listItems({ dax: "BIN", limit: 2 })) ids.push((item as unknown as { Id: number }).Id);

    expect(ids).toEqual([1, 2, 3]);
    expect(seen.map((s) => s.url.searchParams.get("offset"))).toEqual(["0", "2", "3"]);
    expect(seen.every((s) => s.url.searchParams.get("dax") === "BIN" && s.url.searchParams.get("limit") === "2")).toBe(true);
  });

  it("stops a /v1 offset list when a page holds fewer rows than its own limit", async () => {
    const { seen, fetch } = recorder(
      ok({ offset: 0, limit: 2, data: [row(1), row(2)], count: 2 }),
      ok({ offset: 2, limit: 2, data: [row(3)], count: 1 }),
    );
    const fin = clientFor(fetch);

    const pages = [];
    for await (const page of fin.graph.v1.tickers.listPages({ offset: 0, limit: 2 })) pages.push(page);

    expect(pages).toHaveLength(2);
    expect(seen).toHaveLength(2);
    expect(seen[1]!.url.searchParams.get("offset")).toBe("2");
  });

  it("starts an offset walk from the offset given", async () => {
    const { seen, fetch } = recorder(ok({ data: [], isOk: true }));
    const fin = clientFor(fetch);

    for await (const _page of fin.graph.tickers.listPages({ offset: 40 })) void _page;

    expect(seen[0]!.url.searchParams.get("offset")).toBe("40");
  });

  it("surfaces a refusal in the middle of a walk", async () => {
    const { fetch } = recorder(paged([1, 2], 1, 3), refused(402, { error: "insufficient_credits", message: "No credits." }));
    const fin = new FinClient({ news: { apiKey: "n" }, fetch, maxRetries: 0 });

    const seen: number[] = [];
    const walk = async () => { for await (const page of fin.news.news.listPages()) seen.push(page.data?.length ?? 0); };

    await expect(walk()).rejects.toMatchObject({ statusCode: 402, error: "insufficient_credits" });
    expect(seen).toEqual([2]);
  });
});
