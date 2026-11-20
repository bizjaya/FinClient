using Fin;
using Fin.Social;

var apiKey = Environment.GetEnvironmentVariable("FINSOCIAL_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set FINSOCIAL_API_KEY before running this example.");
    return;
}

using var fin = new FinClient(new FinClientOptions { Social = new FinServiceOptions(apiKey) });

try
{
    // The qry object filters and pages the list; the filter fields are the endpoint's own.
    var posts = await fin.Social.Social.PostListAsync(new QueApiSocPostList
    {
        Ticker = "BTC",
        PageSize = 20,
        SortBy = [["published", "desc"]],
    });
    foreach (var post in posts.Data ?? [])
        Console.WriteLine(post.Id);

    // Every open market, a page at a time.
    await foreach (var page in fin.Social.Markets.MarketListPagesAsync(new QueApiSocMarketList { PageSize = 100 }))
    {
        Console.WriteLine($"Page {page.Paging?.PageNo} of {page.Paging?.PageCount}: {page.Data?.Count} markets");
        break;
    }

    // A /v1 call: included in paid plans, gated by package tier.
    var pulse = await fin.Social.V1.Markets.PulseListAsync(limit: 10);
    Console.WriteLine(pulse.Data);
}
catch (FinApiException error)
{
    // 401 invalid key, 402 out of credits, 403 package does not allow the endpoint, 429 rate limit.
    Console.Error.WriteLine($"{error.StatusCode} {error.Error} {error.Message} {error.RetryAfter}");
}
