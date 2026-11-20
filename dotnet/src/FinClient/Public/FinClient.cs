using System.Net.Http;
using Fin.Copy;
using Fin.Graph;
using Fin.News;
using Fin.Social;

namespace Fin;

/// <summary>
/// The client for the Fin services' public APIs: <see cref="Copy"/> reaches FinCopy, <see cref="Graph"/> FinGraph,
/// <see cref="News"/> FinNews and <see cref="Social"/> FinSocial. Create one per set of API keys and reuse it; it is safe
/// to share between threads. A service whose key was not given throws a clear error when you first call it.
/// </summary>
public sealed class FinClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly List<FinHttpTransport> _serviceTransports = new();
    internal FinHttpTransport Transport { get; }

    /// <summary>FinCopy: accounts, orders, the change feed and webhooks.</summary>
    public FinCopyResource Copy { get; }

    /// <summary>FinGraph: market data, tickers, signals and statistics.</summary>
    public FinGraphResource Graph { get; }

    /// <summary>FinNews: news, sentiment, trending, sources and videos.</summary>
    public FinNewsResource News { get; }

    /// <summary>FinSocial: prediction markets, social posts, authors and videos.</summary>
    public FinSocialResource Social { get; }

    /// <summary>Creates a client with its own connection pool.</summary>
    public FinClient(FinClientOptions options) : this(options, (HttpMessageHandler?)null) { }

    /// <summary>Creates a client with an optional handler, useful for custom networking and tests.</summary>
    public FinClient(FinClientOptions options, HttpMessageHandler? handler)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _http = CreateOwnedClient(handler);
        _ownsHttp = true;
        Transport = new FinHttpTransport(_http, options);
        Copy = new FinCopyResource(Transport);
        (Graph, News, Social) = CreateServices(_http, options);
    }

    /// <summary>
    /// Creates a client around a caller-owned <see cref="HttpClient"/>, which it never disposes. Set <see cref="FinClientOptions.ApiKey"/>
    /// for API-key access, or configure this client to send the signed-in member session cookie. That client's own
    /// <see cref="HttpClient.Timeout"/> applies as well as <see cref="FinClientOptions.Timeout"/>.
    /// </summary>
    public FinClient(FinClientOptions options, HttpClient httpClient)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttp = false;
        Transport = new FinHttpTransport(_http, options, allowSession: true);
        Copy = new FinCopyResource(Transport);
        (Graph, News, Social) = CreateServices(_http, options);
    }

    private (FinGraphResource, FinNewsResource, FinSocialResource) CreateServices(HttpClient http, FinClientOptions options)
    {
        FinHttpTransport For(FinServiceOptions? own, FinServiceDefaults service, string block)
        {
            var transport = new FinHttpTransport(http, options,
                new FinServiceConfig(service.Name, FinServices.KeyHeader, own?.ApiKey, FinClientOptions.ServiceUri(own, service),
                    keyRequiredAtUse: true, keyHint: $"FinClientOptions.{block}.ApiKey"));
            _serviceTransports.Add(transport);
            return transport;
        }
        return (new FinGraphResource(For(options.Graph, FinServices.Graph, "Graph")),
            new FinNewsResource(For(options.News, FinServices.News, "News")),
            new FinSocialResource(For(options.Social, FinServices.Social, "Social")));
    }

    /// <summary>Replaces the wait between retries on every service, so tests run without real delays.</summary>
    internal void SetDelay(Func<TimeSpan, CancellationToken, Task> delay)
    {
        Transport.Delay = delay;
        foreach (var transport in _serviceTransports) transport.Delay = delay;
    }

    /// <summary>Disposes the client's own HTTP client. A caller-supplied HttpClient stays with its owner.</summary>
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private static HttpClient CreateOwnedClient(HttpMessageHandler? handler)
    {
        HttpClient client;
        if (handler is not null) client = new HttpClient(handler, disposeHandler: false);
        else
        {
#if NET8_0_OR_GREATER
            // A long-lived client re-resolves DNS by recycling its connections.
            client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: true);
#else
            client = new HttpClient();
#endif
        }
        // FinClientOptions.Timeout governs each attempt; HttpClient's own 100-second limit would cut it short.
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        return client;
    }
}
