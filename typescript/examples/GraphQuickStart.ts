import { FinClient } from "../src/index.js";

const apiKey = process.env.FINGRAPH_API_KEY;
if (!apiKey) throw new Error("Set FINGRAPH_API_KEY in the server environment before running this example.");

const fin = new FinClient({ graph: { apiKey } });

// A /v1 call: included in paid plans, gated by package tier.
const exchanges = await fin.graph.v1.assets.exchanges();
console.log(`Exchanges: ${exchanges.data?.length ?? 0}`);

// An /api call: charged in credits per call, on any plan. The options are the endpoint's named query fields.
const tickers = await fin.graph.tickers.list({ dax: "BIN", quote: "USDT", limit: 5 });
for (const ticker of tickers.data ?? []) console.log(ticker.Symbol, ticker.LastPrice);

// Path arguments are positional.
const detail = await fin.graph.tickers.view("BTCUSDT");
console.log(detail.data?.Symbol);

// A list that pages by limit and offset: the iterator moves offset on until a page comes back empty.
let shown = 0;
for await (const asset of fin.graph.assets.listItems({ dax: "BIN", limit: 100 })) {
  console.log(asset.Symbol);
  if (++shown >= 250) break;
}
