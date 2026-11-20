import { FinClient } from "../src/index.js";

const apiKey = process.env.FINNEWS_API_KEY;
if (!apiKey) throw new Error("Set FINNEWS_API_KEY in the server environment before running this example.");

const fin = new FinClient({ news: { apiKey } });

// The qry object: paging, sorting and this endpoint's filters, typed from the API's document. It is sent as one JSON value.
const latest = await fin.news.news.list({
  pageNo: 1,
  pageSize: 10,
  sortBy: [["published", "desc"]],
  Symbols: "AAPL,MSFT",
});
for (const article of latest.data ?? []) console.log(article.Title);
console.log(`Pages: ${latest.paging?.pageCount}, articles: ${latest.paging?.resCount}`);

// Walk every article that matches, a page at a time, however many pages there are.
let shown = 0;
for await (const article of fin.news.news.listItems({ pageSize: 50, Categories: "markets" })) {
  console.log(article.Checksum, article.Title);
  if (++shown >= 200) break;
}

// A /v1 call: included in paid plans, gated by package tier.
const trending = await fin.news.v1.trending.topics({ Hours: 24, Top: 10 });
console.log(trending.data?.length);

// This request is charged like any other call; ReqSite and ReqUsno are optional.
// await fin.news.news.ratingMake("<checksum>", {});
