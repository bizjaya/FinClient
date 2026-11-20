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

    await fin.graph.assets.exchanges();
    await fin.news.categories.list();
    await fin.social.videos.videos();

    expect(seen.map((s) => s.url.origin)).toEqual(["https://x.fingraph.io", "https://x.finnews.io", "https://x.finsocial.io"]);
    expect(seen.map((s) => s.headers.get("FinUsrKey"))).toEqual(["graph-key", "news-key", "social-key"]);
    for (const s of seen) expect(s.headers.get("X-Api-Key")).toBeNull();
  });

  it("uses a service block's own baseUrl and keeps FinCopy on X-Api-Key", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = new FinClient({
      apiKey: "copy-key", graph: { apiKey: "g", baseUrl: "https://graph.example.test/" }, fetch, retryDelay: noWait,
    });

    await fin.graph.assets.exchanges();
    await fin.copy.accounts.list();

    expect(seen[0]!.url.href).toBe("https://graph.example.test/api/assets/exchanges");
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

      await expect(fin.graph.assets.exchanges()).resolves.toBeDefined();
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

    await expect(fin.graph.assets.exchanges()).rejects.toThrow(/FinGraph needs an API key.*graph: \{ apiKey \}/);
    await expect(fin.social.pulse.pulse()).rejects.toThrow(/FinSocial needs an API key/);
    expect(seen).toHaveLength(0);
    await expect(fin.news.categories.list()).resolves.toBeDefined();
  });

  it("applies the shared timeout and retry settings to every service", async () => {
    let calls = 0;
    const { fetch } = recorder(() => (++calls < 3 ? new Response("{}", { status: 503 }) : ok({ data: [], isOk: true })));
    const fin = new FinClient({ news: { apiKey: "n" }, fetch, retryDelay: noWait, maxRetries: 2, random: () => 0.5 });

    await fin.news.categories.list();

    expect(calls).toBe(3);
  });
});

describe("the two surfaces", () => {
  it("sends /api operations from the groups and /v1 operations from v1", async () => {
    const { seen, fetch } = recorder(ok({ data: [], code: 200, isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.tickers.list();
    await fin.graph.v1.tickers.list();
    await fin.news.sources.list();
    await fin.news.v1.sources.list();
    await fin.social.pulse.pulse();
    await fin.social.v1.pulse.pulse();

    expect(seen.map((s) => s.url.pathname)).toEqual([
      "/api/tickers", "/v1/tickers", "/api/sources", "/v1/sources", "/api/pulse", "/v1/pulse",
    ]);
    expect(seen.every((s) => s.init.method === "GET")).toBe(true);
  });

  it("puts path arguments in the path, encoded", async () => {
    const { seen, fetch } = recorder(ok({ data: {}, code: 200, isOk: true }));
    const fin = clientFor(fetch);

    await fin.graph.tickers.view("BTC/USDT");
    await fin.graph.v1.assets.get("a b");
    await fin.news.news.view("abc123");
    await fin.social.markets.market("m-1", { days: 5 });
    await fin.graph.screens.eqtUsa("top gain", { page: 2 });

    expect(seen.map((s) => s.url.pathname + s.url.search)).toEqual([
      "/api/tickers/BTC%2FUSDT", "/v1/assets/a%20b", "/api/news/abc123", "/api/markets/m-1?days=5", "/api/screens/eqtusa/top%20gain?page=2",
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

    await fin.graph.assets.list({ exchange: "BINANCE", canTrade: false, size: 25, quote: undefined });
    await fin.graph.ohlcv.view("BTCUSDT", { timeframe: "1d", from: "2026-11-01T00:00:00Z", limit: 10 });
    await fin.graph.markets.eqtUsa({ syms: "NVDA,MSFT", sort: "-chg1d" });

    expect(seen[0]!.url.search).toBe("?exchange=BINANCE&canTrade=false&size=25");
    expect(seen[1]!.url.search).toBe("?timeframe=1d&from=2026-11-01T00%3A00%3A00Z&limit=10");
    expect(seen[2]!.url.pathname).toBe("/api/markets/eqtusa");
    expect(seen[2]!.url.searchParams.get("syms")).toBe("NVDA,MSFT");
    expect(seen[2]!.url.searchParams.get("sort")).toBe("-chg1d");
  });

  it("sends list filters as plain query values and no qry parameter", async () => {
    const { seen, fetch } = recorder(ok({ data: [], paging: { pageNo: 1, pageCount: 1 }, isOk: true }));
    const fin = clientFor(fetch);

    await fin.news.news.list({ syms: "AAPL,MSFT", sort: "-published", page: 2, size: 10, q: undefined });
    await fin.social.v1.posts.posts({ syms: "BTC", stance: "bull" });

    expect(seen[0]!.url.pathname).toBe("/api/news");
    expect(seen[0]!.url.search).toBe("?syms=AAPL%2CMSFT&sort=-published&page=2&size=10");
    expect(seen[1]!.url.pathname).toBe("/v1/posts");
    expect(seen[1]!.url.searchParams.has("qry")).toBe(false);
    expect(seen[1]!.url.searchParams.get("stance")).toBe("bull");
  });

  it("formats a date query value as ISO 8601", async () => {
    const { seen, fetch } = recorder(ok({ data: [], isOk: true }));
    const fin = clientFor(fetch);

    await fin.news.news.list({ since: new Date("2026-11-01T08:00:00+08:00") });

    expect(seen[0]!.url.searchParams.get("since")).toBe("2026-11-01T00:00:00.000Z");
  });

  it("returns the whole documented reply, not just its data", async () => {
    const body = { data: [{ Id: 1 }], paging: { pageNo: 1, pageSize: 10, pageCount: 1, resCount: 1 }, code: 200, isOk: true, errors: [] };
    const { fetch } = recorder(ok(body));
    const fin = clientFor(fetch);

    const reply = await fin.news.news.list();

    expect(reply).toEqual(body);
  });

  it("returns a /v1 reply as it is, with the symbols a lookup did not find", async () => {
    const body = { data: [{ Id: "a" }], NotFound: ["NOPE"], paging: { pageNo: 1, pageSize: 50, pageCount: 1, resCount: 1 }, isOk: true };
    const { fetch } = recorder(ok(body));
    const fin = clientFor(fetch);

    const reply = await fin.graph.v1.markets.eqtUsa({ syms: "A,NOPE" });

    expect(reply).toEqual(body);
    expect(reply.NotFound).toEqual(["NOPE"]);
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
    const network = await rejection(down.news.categories.list());
    expect(network.errorCode).toBe("network");
    expect(network.message).toBe("The FinNews API could not be reached.");

    const { fetch } = recorder(new Response("not json", { status: 200 }));
    const garbled = new FinClient({ news: { apiKey: "n" }, fetch });
    const invalid = await rejection(garbled.news.categories.list());
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

  it("walks a list page by page, advancing page, and stops after the last page", async () => {
    const { seen, fetch } = recorder(paged([1, 2], 1, 3), paged([3, 4], 2, 3), paged([5], 3, 3));
    const fin = clientFor(fetch);

    const pages: number[] = [];
    for await (const page of fin.news.news.listPages({ size: 2, syms: "AAPL" })) pages.push(page.data?.length ?? 0);

    expect(pages).toEqual([2, 2, 1]);
    expect(seen.map((s) => s.url.searchParams.get("page"))).toEqual(["1", "2", "3"]);
    expect(seen.every((s) => s.url.searchParams.get("syms") === "AAPL" && s.url.searchParams.get("size") === "2")).toBe(true);
  });

  it("walks every item, starting from the page the query names", async () => {
    const { seen, fetch } = recorder(paged([3, 4], 2, 3), paged([5], 3, 3));
    const fin = clientFor(fetch);

    const ids: number[] = [];
    for await (const item of fin.news.news.listItems({ page: 2, size: 2 })) ids.push((item as unknown as { Id: number }).Id);

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
    for await (const page of fin.social.markets.marketsPages()) pages.push(page);

    expect(pages).toHaveLength(1);
    expect(seen).toHaveLength(2);
  });

  it("yields nothing for an empty list and does not change the query it was given", async () => {
    const { seen, fetch } = recorder(ok({ data: [], paging: { pageNo: 1, pageCount: 0 }, isOk: true }));
    const fin = clientFor(fetch);
    const query = { size: 5 };

    const items = [];
    for await (const item of fin.news.videos.listItems(query)) items.push(item);

    expect(items).toEqual([]);
    expect(seen).toHaveLength(1);
    expect(query).toEqual({ size: 5 });
  });

  it("walks a path-addressed list with its path argument", async () => {
    const { seen, fetch } = recorder(paged([1, 2], 1, 2), paged([3], 2, 2));
    const fin = clientFor(fetch);

    const ids: number[] = [];
    for await (const item of fin.graph.screens.cryptoItems("top gain", { size: 2 })) ids.push((item as unknown as { Id: number }).Id);

    expect(ids).toEqual([1, 2, 3]);
    expect(seen.map((s) => s.url.pathname)).toEqual(["/api/screens/crypto/top%20gain", "/api/screens/crypto/top%20gain"]);
    expect(seen.map((s) => s.url.searchParams.get("page"))).toEqual(["1", "2"]);
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
