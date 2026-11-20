using System.Text.Json;

namespace Fin;

/// <summary>Describes an unsuccessful Fin API call.</summary>
public sealed class FinApiException : Exception
{
    /// <summary>The HTTP status code returned by the API, or 0 when no response arrived (timeout or network failure).</summary>
    public int StatusCode { get; }

    /// <summary>The FinCopy response code, when present.</summary>
    public int? Code { get; }

    /// <summary>
    /// A short machine-readable reason set by the SDK: <c>timeout</c>, <c>network</c>, <c>rate_limited</c>,
    /// <c>invalid_response</c> or <c>empty_response</c>; null for other API errors.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>How long the API asked the caller to wait (the Retry-After header), when it said.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Validation and service errors returned by the API.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// FinGraph, FinNews and FinSocial: the API's machine-readable error code, such as <c>invalid_api_key</c>,
    /// <c>insufficient_credits</c> or <c>package_not_allowed</c>.
    /// </summary>
    public string? Error { get; }

    /// <summary>On limit refusals: the setting that governs the limit that was hit.</summary>
    public string? Lane { get; }

    /// <summary>On limit refusals: the value that was hit.</summary>
    public JsonElement? Limit { get; }

    /// <summary>On per-category limits: the category tag.</summary>
    public string? Catag { get; }

    /// <summary>On per-category limits: the package tier of the caller.</summary>
    public string? Tier { get; }

    /// <summary>Creates a typed API exception.</summary>
    public FinApiException(int statusCode, string message, int? code = null, IReadOnlyList<string>? errors = null,
        Exception? innerException = null, string? errorCode = null, TimeSpan? retryAfter = null,
        string? error = null, string? lane = null, JsonElement? limit = null, string? catag = null, string? tier = null)
        : base(string.IsNullOrWhiteSpace(message) ? "The Fin API request failed." : message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? Array.Empty<string>();
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
        Error = error;
        Lane = lane;
        Limit = limit;
        Catag = catag;
        Tier = tier;
    }
}
