/** Configuration for a server-side FinCopy API client. */
export interface FinClientOptions {
  /** FinCopy API key. Keep it in trusted server-side code. */
  apiKey: string;
  /** Absolute API root. Defaults to the public FinCopy API. */
  baseUrl?: string;
  /** Per-attempt timeout in milliseconds. Defaults to 30 seconds. */
  timeoutMs?: number;
  /** Retries after the initial request. Defaults to 3. */
  maxRetries?: number;
  /** Optional fetch implementation, primarily useful for tests. */
  fetch?: typeof fetch;
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
