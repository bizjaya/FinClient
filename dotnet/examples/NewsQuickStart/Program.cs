using Fin;
using Fin.News;

var apiKey = Environment.GetEnvironmentVariable("FINNEWS_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set FINNEWS_API_KEY before running this example.");
    return;
}

using var fin = new FinClient(new FinClientOptions { News = new FinServiceOptions(apiKey) });

// Plain query values: symbols, a sort (a minus sign sorts high to low), and the page to read.
var latest = await fin.News.News.ListAsync(syms: "AAPL,MSFT", sort: "-published", page: 1, size: 10);
foreach (var article in latest.Data ?? [])
    Console.WriteLine(article.Title);
Console.WriteLine($"Pages: {latest.Paging?.PageCount}, articles: {latest.Paging?.ResCount}");

// Walk every article that matches, a page at a time, however many pages there are.
var shown = 0;
await foreach (var article in fin.News.News.ListItemsAsync(cats: "markets", size: 50))
{
    Console.WriteLine($"{article.Id} {article.Title}");
    if (++shown >= 200) break;
}

// A /v1 call: included in paid plans, gated by package tier.
var trending = await fin.News.V1.Trending.ListAsync(size: 10);
Console.WriteLine(trending.Data?.Count);
