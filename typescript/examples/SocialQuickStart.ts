import { FinApiError, FinClient } from "../src/index.js";

const apiKey = process.env.FINSOCIAL_API_KEY;
if (!apiKey) throw new Error("Set FINSOCIAL_API_KEY in the server environment before running this example.");

const fin = new FinClient({ social: { apiKey } });

try {
  // The qry object filters and pages the list; the filter fields are the endpoint's own.
  const posts = await fin.social.social.postList({ Ticker: "BTC", pageSize: 20, sortBy: [["published", "desc"]] });
  for (const post of posts.data ?? []) console.log(post.Id);

  // Every open market, a page at a time.
  for await (const page of fin.social.markets.marketListPages({ pageSize: 100 })) {
    console.log(`Page ${page.paging?.pageNo} of ${page.paging?.pageCount}: ${page.data?.length} markets`);
    break;
  }

  // A /v1 call: included in paid plans, gated by package tier.
  const pulse = await fin.social.v1.markets.pulseList({ Limit: 10 });
  console.log(pulse.data);
} catch (error) {
  // 401 invalid key, 402 out of credits, 403 package does not allow the endpoint, 429 rate limit.
  if (error instanceof FinApiError) console.error(error.statusCode, error.error, error.message, error.retryAfterMs);
  else throw error;
}
