using Fin;
using Fin.News;

var apiKey = Environment.GetEnvironmentVariable("FINNEWS_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set FINNEWS_API_KEY before running this example.");
    return;
}

using var fin = new FinClient(new FinClientOptions { News = new FinServiceOptions(apiKey) });

// The qry object: paging, sorting and this endpoint's filters, typed from the API's document. It is sent as one JSON value.
var latest = await fin.News.News.ListAsync(new QueApiNewsList
{
    PageNo = 1,
    PageSize = 10,
    SortBy = [["published", "desc"]],
    Symbols = "AAPL,MSFT",
});
foreach (var article in latest.Data ?? [])
    Console.WriteLine(article.Title);
Console.WriteLine($"Pages: {latest.Paging?.PageCount}, articles: {latest.Paging?.ResCount}");

// Walk every article that matches, a page at a time, however many pages there are.
var shown = 0;
await foreach (var article in fin.News.News.ListItemsAsync(new QueApiNewsList { PageSize = 50, Categories = "markets" }))
{
    Console.WriteLine($"{article.Checksum} {article.Title}");
    if (++shown >= 200) break;
}

// A /v1 call: included in paid plans, gated by package tier.
var trending = await fin.News.V1.Trending.TopicsAsync(hours: 24, top: 10);
Console.WriteLine(trending.Data?.Count);

// This request is charged like any other call; ReqSite and ReqUsno are optional.
// await fin.News.News.RatingMakeAsync("<checksum>", new CmdApiRatingMake());
