/** The key and address of one of the other Fin services (FinGraph, FinNews or FinSocial). */
export interface FinServiceOptions {
  /** The service's API key, sent in the `FinUsrKey` header. Keep it in trusted server-side code. */
  apiKey: string;
  /** Absolute API root. Defaults to `https://x.fingraph.io`, `https://x.finnews.io` or `https://x.finsocial.io`. */
  baseUrl?: string;
}

/**
 * Configuration for a server-side Fin API client. The top-level `apiKey` and `baseUrl` belong to FinCopy; FinGraph, FinNews
 * and FinSocial each have their own block. The timeouts, retries, `fetch` and the rest apply to every service.
 */
export interface FinClientOptions {
  /** FinCopy API key. Keep it in trusted server-side code. Optional when the supplied fetch authenticates a member session, or when a service block below is given. */
  apiKey?: string;
  /** FinCopy's absolute API root. Defaults to the public FinCopy API. */
  baseUrl?: string;
  /** FinGraph: market data, tickers, signals and statistics. */
  graph?: FinServiceOptions;
  /** FinNews: news, sentiment, trending, sources and videos. */
  news?: FinServiceOptions;
  /** FinSocial: prediction markets, social posts, authors and videos. */
  social?: FinServiceOptions;
  /** Per-attempt timeout in milliseconds. Defaults to 30 seconds. */
  timeoutMs?: number;
  /** Retries after the initial request. Defaults to 3. */
  maxRetries?: number;
  /** Optional fetch implementation, primarily useful for tests. */
  fetch?: typeof fetch;
  /** Fetch credentials mode. Session clients default to include; key clients default to same-origin. */
  credentials?: "omit" | "same-origin" | "include";
  /** Optional retry wait implementation, primarily useful for tests. */
  retryDelay?: (milliseconds: number, signal?: AbortSignal) => Promise<void>;
  /** Optional jitter source, primarily useful for tests. */
  random?: () => number;
}

export interface FinRequestOptions {
  signal?: AbortSignal;
  /** Supply a stable key when the same write will be retried by the caller. */
  idempotencyKey?: string;
}
