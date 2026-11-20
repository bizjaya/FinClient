namespace Fin;

/// <summary>The key and address of one of the other Fin services: FinGraph, FinNews or FinSocial.</summary>
public sealed class FinServiceOptions
{
    /// <summary>Creates options with no key yet.</summary>
    public FinServiceOptions() { }

    /// <summary>Creates options with the service's API key.</summary>
    /// <param name="apiKey">The key sent in the FinUsrKey request header.</param>
    public FinServiceOptions(string apiKey) => ApiKey = apiKey;

    /// <summary>The service's API key, sent in the FinUsrKey header. Keep it on the server; never ship it to a browser or an app.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Absolute API root. When null or empty, the service's public address is used: <c>https://x.fingraph.io</c>,
    /// <c>https://x.finnews.io</c> or <c>https://x.finsocial.io</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    internal FinServiceOptions Copy() => new() { ApiKey = ApiKey, BaseUrl = BaseUrl };
}

/// <summary>
/// Configures a Fin API client. The client copies these values when it is created; later changes do not affect it.
/// <see cref="ApiKey"/> and <see cref="BaseUrl"/> belong to FinCopy; FinGraph, FinNews and FinSocial each have their own
/// block. The timeout and retry settings apply to every service.
/// </summary>
public sealed class FinClientOptions
{
    /// <summary>Creates options for the default FinCopy API address.</summary>
    public FinClientOptions() { }

    /// <summary>Creates options with a FinCopy API key.</summary>
    /// <param name="apiKey">The key sent in the X-Api-Key request header.</param>
    public FinClientOptions(string apiKey) => ApiKey = apiKey;

    /// <summary>FinCopy API key sent in the X-Api-Key header. Keep it on the server; never ship it to a browser or an app.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>FinCopy's absolute API root. Defaults to the public FinCopy API.</summary>
    public string BaseUrl { get; set; } = "https://api.fincopy.io";

    /// <summary>FinGraph: market data, tickers, signals and statistics. Null when you do not use it.</summary>
    public FinServiceOptions? Graph { get; set; }

    /// <summary>FinNews: news, sentiment, trending, sources and videos. Null when you do not use it.</summary>
    public FinServiceOptions? News { get; set; }

    /// <summary>FinSocial: prediction markets, social posts, authors and videos. Null when you do not use it.</summary>
    public FinServiceOptions? Social { get; set; }

    /// <summary>
    /// Per-attempt request timeout. Defaults to 30 seconds. When you pass your own <see cref="HttpClient"/>, its
    /// <see cref="HttpClient.Timeout"/> also applies.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum retries after the initial request. Defaults to 3.</summary>
    public int MaxRetries { get; set; } = 3;

    internal bool HasServiceBlock => Graph is not null || News is not null || Social is not null;

    internal FinClientOptions Snapshot() => new()
    {
        ApiKey = ApiKey,
        BaseUrl = BaseUrl,
        Graph = Graph?.Copy(),
        News = News?.Copy(),
        Social = Social?.Copy(),
        Timeout = Timeout,
        MaxRetries = MaxRetries
    };

    internal Uri ValidatedBaseUri(bool requireApiKey = true)
    {
        // A service block lets the client work without FinCopy's key: using FinCopy then fails at its first call.
        if (requireApiKey && !HasServiceBlock && string.IsNullOrWhiteSpace(ApiKey))
            throw new ArgumentException("An API key is required unless the caller supplies an authenticated HttpClient.", nameof(ApiKey));
        var uri = ValidatedUri(BaseUrl, nameof(BaseUrl));
        if (Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive.");
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries cannot be negative.");
        _ = ServiceUri(Graph, FinServices.Graph);
        _ = ServiceUri(News, FinServices.News);
        _ = ServiceUri(Social, FinServices.Social);
        return uri;
    }

    internal static Uri ServiceUri(FinServiceOptions? options, FinServiceDefaults service) =>
        ValidatedUri(string.IsNullOrWhiteSpace(options?.BaseUrl) ? service.BaseUrl : options!.BaseUrl!, service.Name + " BaseUrl");

    private static Uri ValidatedUri(string? baseUrl, string name)
    {
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("BaseUrl must be an absolute HTTP or HTTPS URL without a query or fragment.", name);
        return uri;
    }
}

/// <summary>What is fixed about one of the other Fin services: its name and public address.</summary>
internal sealed class FinServiceDefaults
{
    public FinServiceDefaults(string name, string baseUrl) { Name = name; BaseUrl = baseUrl; }
    public string Name { get; }
    public string BaseUrl { get; }
}

internal static class FinServices
{
    /// <summary>FinGraph, FinNews and FinSocial take their key in this header; FinCopy takes X-Api-Key.</summary>
    internal const string KeyHeader = "FinUsrKey";
    internal static readonly FinServiceDefaults Graph = new("FinGraph", "https://x.fingraph.io");
    internal static readonly FinServiceDefaults News = new("FinNews", "https://x.finnews.io");
    internal static readonly FinServiceDefaults Social = new("FinSocial", "https://x.finsocial.io");
}
