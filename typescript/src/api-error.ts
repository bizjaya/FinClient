/** A short machine-readable reason set by the SDK; the same names as the .NET client's `ErrorCode`. */
export type FinErrorCode = "timeout" | "network" | "rate_limited" | "invalid_response" | "empty_response";

/** Details an API error can carry beyond its status and message. */
export interface FinApiErrorDetails {
  errorCode?: FinErrorCode | null;
  retryAfterMs?: number | null;
  cause?: unknown;
  /** The API's machine-readable error code, such as `insufficient_credits`. */
  error?: string | null;
  /** On limit refusals: the setting that governs the limit that was hit. */
  lane?: string | null;
  /** On limit refusals: the value that was hit. */
  limit?: unknown;
  /** On per-category limits: the category tag. */
  catag?: string | null;
  /** On per-category limits: the package tier of the caller. */
  tier?: string | null;
}

/** An unsuccessful Fin API call. */
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
  /** FinGraph, FinNews and FinSocial: the API's machine-readable error code (`invalid_api_key`, `insufficient_credits`, `package_not_allowed`, ...). */
  readonly error: string | null;
  /** On limit refusals: the setting that governs the limit that was hit. */
  readonly lane: string | null;
  /** On limit refusals: the value that was hit. */
  readonly limit: unknown;
  /** On per-category limits: the category tag. */
  readonly catag: string | null;
  /** On per-category limits: the package tier of the caller. */
  readonly tier: string | null;

  constructor(
    statusCode: number,
    message: string,
    code: number | string | null = null,
    errors: readonly string[] = [],
    details: FinApiErrorDetails = {},
  ) {
    super(message || "The Fin API request failed.", details.cause === undefined ? undefined : { cause: details.cause });
    this.name = "FinApiError";
    this.statusCode = statusCode;
    this.code = code;
    this.errors = errors;
    this.errorCode = details.errorCode ?? null;
    this.retryAfterMs = details.retryAfterMs ?? null;
    this.error = details.error ?? null;
    this.lane = details.lane ?? null;
    this.limit = details.limit;
    this.catag = details.catag ?? null;
    this.tier = details.tier ?? null;
  }
}
