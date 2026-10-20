namespace Fin;

/// <summary>Describes an unsuccessful FinCopy API call.</summary>
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

    /// <summary>Creates a typed API exception.</summary>
    public FinApiException(int statusCode, string message, int? code = null, IReadOnlyList<string>? errors = null,
        Exception? innerException = null, string? errorCode = null, TimeSpan? retryAfter = null)
        : base(string.IsNullOrWhiteSpace(message) ? "The FinCopy API request failed." : message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? Array.Empty<string>();
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
    }
}
