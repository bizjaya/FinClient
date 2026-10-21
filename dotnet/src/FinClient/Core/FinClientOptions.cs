namespace Fin;

/// <summary>Configures a FinCopy API client. The client copies these values when it is created; later changes do not affect it.</summary>
public sealed class FinClientOptions
{
    /// <summary>Creates options for the default FinCopy API address.</summary>
    public FinClientOptions() { }

    /// <summary>Creates options with a FinCopy API key.</summary>
    /// <param name="apiKey">The key sent in the X-Api-Key request header.</param>
    public FinClientOptions(string apiKey) => ApiKey = apiKey;

    /// <summary>FinCopy API key sent in the X-Api-Key header. Keep it on the server; never ship it to a browser or an app.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Absolute API root. Defaults to the public FinCopy API.</summary>
    public string BaseUrl { get; set; } = "https://api.fincopy.pro";

    /// <summary>
    /// Per-attempt request timeout. Defaults to 30 seconds. When you pass your own <see cref="HttpClient"/>, its
    /// <see cref="HttpClient.Timeout"/> also applies.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum retries after the initial request. Defaults to 3.</summary>
    public int MaxRetries { get; set; } = 3;

    internal FinClientOptions Snapshot() => new()
    {
        ApiKey = ApiKey,
        BaseUrl = BaseUrl,
        Timeout = Timeout,
        MaxRetries = MaxRetries
    };

    internal Uri ValidatedBaseUri(bool requireApiKey = true)
    {
        if (requireApiKey && string.IsNullOrWhiteSpace(ApiKey)) throw new ArgumentException("An API key is required unless the caller supplies an authenticated HttpClient.", nameof(ApiKey));
        if (!Uri.TryCreate(BaseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("BaseUrl must be an absolute HTTP or HTTPS URL without a query or fragment.", nameof(BaseUrl));
        if (Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive.");
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries cannot be negative.");
        return uri;
    }
}
