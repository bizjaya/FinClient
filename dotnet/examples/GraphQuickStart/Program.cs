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

// An /api call: charged in credits per call, on any plan. The query fields are named arguments.
var tickers = await fin.Graph.Tickers.ListAsync(dax: DAX.BIN, quote: "USDT", limit: 5);
foreach (var ticker in tickers.Data ?? [])
    Console.WriteLine($"{ticker.Symbol} {ticker.LastPrice}");

// Path arguments are positional.
var detail = await fin.Graph.Tickers.ViewAsync("BTCUSDT");
Console.WriteLine(detail.Data?.Symbol);

// A list that pages by limit and offset: the iterator moves offset on until a page comes back empty.
var shown = 0;
await foreach (var asset in fin.Graph.Assets.ListItemsAsync(dax: DAX.BIN, limit: 100))
{
    Console.WriteLine(asset.Symbol);
    if (++shown >= 250) break;
}
