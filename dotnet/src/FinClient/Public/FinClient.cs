using System.Net.Http;
using Fin.Copy;

namespace Fin;

/// <summary>
/// The client for the Fin services' public APIs. <see cref="Copy"/> reaches FinCopy. Create one per API key and reuse it;
/// it is safe to share between threads.
/// </summary>
public sealed class FinClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    internal FinHttpTransport Transport { get; }

    /// <summary>FinCopy: accounts, orders, the change feed and webhooks.</summary>
    public FinCopyResource Copy { get; }

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
