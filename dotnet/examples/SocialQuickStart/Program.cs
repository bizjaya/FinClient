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
    // What is being said about two symbols: mentions, engagement and the bullish and bearish split.
    var pulse = await fin.Social.Pulse.PulseAsync(syms: "BTC,ETH");
    foreach (var row in pulse.Data ?? [])
        Console.WriteLine($"{row.Sym} {row.Mentions} {row.Bullish} {row.Bearish}");
    Console.WriteLine($"Not found: {string.Join(", ", pulse.NotFound ?? [])}");

    // Posts about one symbol.
    var posts = await fin.Social.Posts.PostsAsync(syms: "BTC", size: 20);
    foreach (var post in posts.Data ?? [])
        Console.WriteLine($"{post.Id} {post.Author}");

    // Every prediction market, a page at a time.
    await foreach (var page in fin.Social.Markets.MarketsPagesAsync(size: 50))
    {
        Console.WriteLine($"Page {page.Paging?.PageNo} of {page.Paging?.PageCount}: {page.Data?.Count} markets");
        break;
    }

    // A /v1 call: included in paid plans, gated by package tier.
    var creators = await fin.Social.V1.Creators.CreatorsAsync(size: 10);
    Console.WriteLine(creators.Data?.Count);
}
catch (FinApiException error)
{
    // 401 invalid key, 402 out of credits, 403 package does not allow the endpoint, 429 rate limit.
    Console.Error.WriteLine($"{error.StatusCode} {error.Error} {error.Message} {error.RetryAfter}");
}
