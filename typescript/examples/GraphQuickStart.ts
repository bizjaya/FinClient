import { FinClient } from "../src/index.js";

const apiKey = process.env.FINGRAPH_API_KEY;
if (!apiKey) throw new Error("Set FINGRAPH_API_KEY in the server environment before running this example.");

const fin = new FinClient({ graph: { apiKey } });

// A /v1 call: included in paid plans, gated by package tier.
const exchanges = await fin.graph.v1.assets.exchanges();
console.log(`Exchanges: ${exchanges.data?.length ?? 0}`);

// US stocks by name: up to 50 symbols, sorted by one-day change, highest first. Names no row carries come back in NotFound.
const stocks = await fin.graph.markets.eqtUsa({ syms: "NVDA,MSFT,BCDA", sort: "-chg1d" });
for (const stock of stocks.data ?? []) console.log(stock.Sym, stock.Price, stock.Signal);
console.log(`Not found: ${stocks.NotFound?.join(", ") ?? "none"}`);

// Path arguments are positional.
const detail = await fin.graph.tickers.view("BTCUSDT");
console.log(detail.data?.Symbol);

// A list pages with page and size: the iterator moves page on until the last page the paging block names.
let shown = 0;
for await (const asset of fin.graph.assets.listItems({ exchange: "BINANCE", size: 50 })) {
  console.log(asset.Symbol);
  if (++shown >= 250) break;
}
