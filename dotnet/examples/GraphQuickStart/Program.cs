using Fin;
using Fin.Graph;

var apiKey = Environment.GetEnvironmentVariable("FINGRAPH_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set FINGRAPH_API_KEY before running this example.");
    return;
}

using var fin = new FinClient(new FinClientOptions { Graph = new FinServiceOptions(apiKey) });

// A /v1 call: included in paid plans, gated by package tier.
var exchanges = await fin.Graph.V1.Assets.ExchangesAsync();
Console.WriteLine($"Exchanges: {exchanges.Data?.Count ?? 0}");

// US stocks by name: up to 50 symbols, sorted by one-day change, highest first. Names no row carries come back in NotFound.
var stocks = await fin.Graph.Markets.EqtUsaAsync(syms: "NVDA,MSFT,BCDA", sort: "-chg1d");
foreach (var stock in stocks.Data ?? [])
    Console.WriteLine($"{stock.Sym} {stock.Price} {stock.Signal}");
Console.WriteLine($"Not found: {string.Join(", ", stocks.NotFound ?? [])}");

// Path arguments are positional.
var detail = await fin.Graph.Tickers.ViewAsync("BTCUSDT");
Console.WriteLine(detail.Data?.Symbol);

// A list pages with page and size: the iterator moves page on until the last page the paging block names.
var shown = 0;
await foreach (var asset in fin.Graph.Assets.ListItemsAsync(exchange: "BINANCE", size: 50))
{
    Console.WriteLine(asset.Symbol);
    if (++shown >= 250) break;
}
