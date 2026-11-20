import { FinApiError, FinClient } from "../src/index.js";

const apiKey = process.env.FINSOCIAL_API_KEY;
if (!apiKey) throw new Error("Set FINSOCIAL_API_KEY in the server environment before running this example.");

const fin = new FinClient({ social: { apiKey } });

try {
  // What is being said about two symbols: mentions, engagement and the bullish and bearish split.
  const pulse = await fin.social.pulse.pulse({ syms: "BTC,ETH" });
  for (const row of pulse.data ?? []) console.log(row.Sym, row.Mentions, row.Bullish, row.Bearish);
  console.log(`Not found: ${pulse.NotFound?.join(", ") ?? "none"}`);

  // Posts about one symbol, newest first.
  const posts = await fin.social.posts.posts({ syms: "BTC", size: 20 });
  for (const post of posts.data ?? []) console.log(post.Id, post.Author);

  // Every prediction market, a page at a time.
  for await (const page of fin.social.markets.marketsPages({ size: 50 })) {
    console.log(`Page ${page.paging?.pageNo} of ${page.paging?.pageCount}: ${page.data?.length} markets`);
    break;
  }

  // A /v1 call: included in paid plans, gated by package tier.
  const creators = await fin.social.v1.creators.creators({ size: 10 });
  console.log(creators.data?.length);
} catch (error) {
  // 401 invalid key, 402 out of credits, 403 package does not allow the endpoint, 429 rate limit.
  if (error instanceof FinApiError) console.error(error.statusCode, error.error, error.message, error.retryAfterMs);
  else throw error;
}
