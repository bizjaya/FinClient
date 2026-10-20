/** A short machine-readable reason set by the SDK; the same names as the .NET client's `ErrorCode`. */
export type FinErrorCode = "timeout" | "network" | "rate_limited" | "invalid_response" | "empty_response";

/** An unsuccessful FinCopy API call. */
export class FinApiError extends Error {
  /** The HTTP status code returned by the API, or 0 when no response arrived (timeout or network failure). */
  readonly statusCode: number;
  /** The FinCopy response code, when present. */
  readonly code: number | string | null;
  /** Validation and service errors returned by the API. */
  readonly errors: readonly string[];
  /** Set by the SDK for timeouts, network failures, rate limits and malformed responses; null for other API errors. */
  readonly errorCode: FinErrorCode | null;
  /** How long the API asked the caller to wait (the Retry-After header), in milliseconds, when it said. */
  readonly retryAfterMs: number | null;

  constructor(
    statusCode: number,
    message: string,
    code: number | string | null = null,
    errors: readonly string[] = [],
    details: { errorCode?: FinErrorCode | null; retryAfterMs?: number | null; cause?: unknown } = {},
  ) {
    super(message || "The FinCopy API request failed.", details.cause === undefined ? undefined : { cause: details.cause });
    this.name = "FinApiError";
    this.statusCode = statusCode;
    this.code = code;
    this.errors = errors;
    this.errorCode = details.errorCode ?? null;
    this.retryAfterMs = details.retryAfterMs ?? null;
  }
}
