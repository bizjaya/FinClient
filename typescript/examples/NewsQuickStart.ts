import { FinClient } from "../src/index.js";

const apiKey = process.env.FINNEWS_API_KEY;
if (!apiKey) throw new Error("Set FINNEWS_API_KEY in the server environment before running this example.");

const fin = new FinClient({ news: { apiKey } });

// Plain query values: symbols, a sort (a minus sign sorts high to low), and the page to read.
const latest = await fin.news.news.list({ syms: "AAPL,MSFT", sort: "-published", page: 1, size: 10 });
for (const article of latest.data ?? []) console.log(article.Title);
console.log(`Pages: ${latest.paging?.pageCount}, articles: ${latest.paging?.resCount}`);

// Walk every article that matches, a page at a time, however many pages there are.
let shown = 0;
for await (const article of fin.news.news.listItems({ cats: "markets", size: 50 })) {
  console.log(article.Id, article.Title);
  if (++shown >= 200) break;
}

// A /v1 call: included in paid plans, gated by package tier.
const trending = await fin.news.v1.trending.list({ size: 10 });
console.log(trending.data?.length);
