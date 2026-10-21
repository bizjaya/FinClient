import { randomUUID } from "node:crypto";
import { FinApiError } from "./api-error.js";
import type { FinClientOptions, FinRequestOptions } from "./options.js";
import type { components } from "./generated/schema.js";

interface FinEnvelope<T> {
  data?: T;
  code?: number;
  isOk?: boolean;
  message?: string | null;
  errors?: string[] | null;
  chksum?: string | null;
  paging?: FinPageInfo | null;
}

/** The paging block of a list reply, as the API sends it (camel-case, like the rest of the envelope). */
export type FinPageInfo = components["schemas"]["ResPage"];

/** One server-sent event from the FinCopy member stream. */
export interface FinStreamEvent { event: string; data: string }

/** A paged response from a FinCopy list endpoint. DTO members retain their wire casing. */
export interface FinPage<T> {
  items: readonly T[];
  paging: FinPageInfo | null;
  code: number;
  isOk: true;
  message: string | null;
  errors: readonly string[];
  chksum: string | null;
}

const retryableStatuses = new Set([429, 502, 503, 504]);
const writeMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/** Shared authenticated transport. Resource methods provide the route and generated data type. */
export class FinHttpTransport {
  private readonly apiKey: string;
  private readonly baseUrl: string;
  private readonly credentials: "omit" | "same-origin" | "include";
  private readonly timeoutMs: number;
  private readonly maxRetries: number;
  private readonly fetcher: typeof fetch;
  private readonly delay: (milliseconds: number, signal?: AbortSignal) => Promise<void>;
  private readonly random: () => number;

  constructor(options: FinClientOptions) {
    this.apiKey = options.apiKey?.trim() ?? "";
    if (!this.apiKey && !options.fetch) throw new TypeError("apiKey is required unless a custom fetch authenticates the member session.");
    this.baseUrl = (options.baseUrl ?? "https://api.fincopy.pro").replace(/\/+$/, "");
    const base = new URL(this.baseUrl);
    if (!/^https?:$/.test(base.protocol) || base.search || base.hash) {
      throw new TypeError("baseUrl must be an absolute HTTP or HTTPS URL without a query or fragment.");
    }
    this.timeoutMs = options.timeoutMs ?? 30_000;
    this.maxRetries = options.maxRetries ?? 3;
    if (!Number.isFinite(this.timeoutMs) || this.timeoutMs <= 0) throw new RangeError("timeoutMs must be positive.");
    if (!Number.isInteger(this.maxRetries) || this.maxRetries < 0) throw new RangeError("maxRetries cannot be negative.");
    this.fetcher = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.credentials = options.credentials ?? (this.apiKey ? "same-origin" : "include");
    this.delay = options.retryDelay ?? ((milliseconds, signal) => new Promise<void>((resolve, reject) => {
      if (signal?.aborted) { reject(signal.reason); return; }
      const onAbort = () => { clearTimeout(timer); reject(signal?.reason); };
      const timer = setTimeout(() => {
        signal?.removeEventListener("abort", onAbort);
        resolve();
      }, milliseconds);
      signal?.addEventListener("abort", onAbort, { once: true });
      if (signal?.aborted) onAbort();
    }));
    this.random = options.random ?? Math.random;
  }

  async request<T>(method: string, path: string, body?: unknown, requestOptions: FinRequestOptions = {}): Promise<T> {
    const envelope = await this.sendEnvelope<T>(method, path, body, requestOptions);
    return envelope.data as T;
  }

  private async sendEnvelope<T>(method: string, path: string, body?: unknown, requestOptions: FinRequestOptions = {}): Promise<FinEnvelope<T>> {
    const verb = method.toUpperCase();
    const writes = writeMethods.has(verb);
    // Unset fields are left out rather than sent as null; FinCopy reads a missing field and a null one the same way.
    const bodyText = body === undefined ? undefined : JSON.stringify(body, (_key, value) => (value === null ? undefined : value));
    const idempotencyKey = writes ? idempotencyKeyOf(requestOptions.idempotencyKey) : undefined;
    const url = new URL(path.replace(/^\//, ""), `${this.baseUrl}/`);

    for (let retry = 0; ; retry++) {
      if (requestOptions.signal?.aborted) throw requestOptions.signal.reason;
      const controller = new AbortController();
      const onAbort = () => controller.abort(requestOptions.signal?.reason);
      requestOptions.signal?.addEventListener("abort", onAbort, { once: true });
      if (requestOptions.signal?.aborted) onAbort();
      let timedOut = false;
      const timer = setTimeout(() => { timedOut = true; controller.abort(new Error("FinCopy request timed out.")); }, this.timeoutMs);
      let response: Response;
      let raw: string;
      try {
        const headers = new Headers({ Accept: "application/json" });
        if (this.apiKey) headers.set("X-Api-Key", this.apiKey);
        if (bodyText !== undefined) headers.set("Content-Type", "application/json");
        if (idempotencyKey !== undefined) headers.set("Idempotency-Key", idempotencyKey);
        response = await this.fetcher(url, {
          method: verb,
          headers,
          body: bodyText,
          signal: controller.signal,
          credentials: this.credentials,
        });
        raw = await response.text();
      } catch (error) {
        // The caller's own cancellation is never retried or wrapped.
        if (requestOptions.signal?.aborted) throw error;
        if (retry >= this.maxRetries) {
          throw timedOut
            ? new FinApiError(0, "The FinCopy API did not answer in time.", null, [], { errorCode: "timeout", cause: error })
            : new FinApiError(0, "The FinCopy API could not be reached.", null, [], { errorCode: "network", cause: error });
        }
        await this.pause(this.backoff(retry), requestOptions.signal);
        continue;
      } finally {
        clearTimeout(timer);
        requestOptions.signal?.removeEventListener("abort", onAbort);
      }

      const serverWait = retryAfterMsOf(response.headers.get("Retry-After"));
      if (retryableStatuses.has(response.status)) {
        if (retry < this.maxRetries && (serverWait === null || serverWait <= maxServerRetryAfterMs)) {
          await this.pause(serverWait ?? (response.status === 429 ? rateLimitBackoff(retry) : this.backoff(retry)), requestOptions.signal);
          continue;
        }
      }

      let envelope: FinEnvelope<T> | null = null;
      try { envelope = raw ? JSON.parse(raw) as FinEnvelope<T> : null; } catch { /* mapped to a typed API error below */ }
      if (!response.ok || !envelope || envelope.isOk !== true) {
        const fallback = `The FinCopy API request failed with HTTP ${response.status}.`;
        const message = typeof envelope?.message === "string" && envelope.message ? envelope.message : fallback;
        const errorCode = response.status === 429 ? "rate_limited" : response.ok ? "invalid_response" : null;
        throw new FinApiError(response.status, message, envelope?.code ?? null, errorsOf(envelope?.errors),
          { errorCode, retryAfterMs: serverWait });
      }
      if (!Object.prototype.hasOwnProperty.call(envelope, "data") || envelope.data === null) {
        throw new FinApiError(response.status, "The FinCopy API returned an empty response.", envelope.code ?? null,
          errorsOf(envelope.errors), { errorCode: "empty_response" });
      }
      return envelope;
    }
  }

  async requestPage<T>(method: string, path: string, requestOptions: FinRequestOptions = {}): Promise<FinPage<T>> {
    const envelope = await this.sendEnvelope<T[]>(method, path, undefined, requestOptions);
    if (!Array.isArray(envelope.data)) {
      throw new FinApiError(200, "The FinCopy API returned an invalid page.", envelope.code ?? null, envelope.errors ?? []);
    }
    return {
      items: envelope.data,
      paging: envelope.paging ?? null,
      code: envelope.code ?? 200,
      isOk: true,
      message: envelope.message ?? null,
      errors: envelope.errors ?? [],
      chksum: envelope.chksum ?? null,
    };
  }

  async *requestEvents(path: string, requestOptions: FinRequestOptions = {}): AsyncGenerator<FinStreamEvent, void, undefined> {
    const url = new URL(path.replace(/^\//, ""), `${this.baseUrl}/`);
    const headers = new Headers({ Accept: "text/event-stream" });
    if (this.apiKey) headers.set("X-Api-Key", this.apiKey);
    const response = await this.fetcher(url, {
      method: "GET", headers, signal: requestOptions.signal, credentials: this.credentials,
    });
    if (!response.ok) {
      const raw = await response.text();
      let envelope: FinEnvelope<unknown> | null = null;
      try { envelope = raw ? JSON.parse(raw) as FinEnvelope<unknown> : null; } catch { /* report as a typed API error */ }
      const fallback = `The FinCopy API request failed with HTTP ${response.status}.`;
      throw new FinApiError(response.status, envelope?.message || fallback, envelope?.code ?? null,
        errorsOf(envelope?.errors), { errorCode: response.status === 429 ? "rate_limited" : null,
          retryAfterMs: retryAfterMsOf(response.headers.get("Retry-After")) });
    }
    if (!response.body) throw new FinApiError(response.status, "The FinCopy API returned an empty event stream.", null, [], { errorCode: "empty_response" });
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let eventName = "message";
    let data: string[] = [];
    const parseLine = (line: string): FinStreamEvent | null => {
      if (!line) {
        const item = data.length ? { event: eventName, data: data.join("\n") } : null;
        eventName = "message";
        data = [];
        return item;
      }
      if (line.startsWith(":")) return null;
      const separator = line.indexOf(":");
      const field = separator < 0 ? line : line.slice(0, separator);
      const value = separator < 0 ? "" : line.slice(separator + 1).replace(/^ /, "");
      if (field === "event") eventName = value;
      else if (field === "data") data.push(value);
      return null;
    };
    try {
      while (true) {
        if (requestOptions.signal?.aborted) throw requestOptions.signal.reason;
        const next = await reader.read();
        buffer += decoder.decode(next.value, { stream: !next.done });
        const lines = buffer.split(/\r?\n/);
        buffer = lines.pop() ?? "";
        for (const line of lines) {
          const item = parseLine(line);
          if (item) yield item;
        }
        if (next.done) {
          if (buffer) { const item = parseLine(buffer); if (item) yield item; }
          const final = parseLine("");
          if (final) yield final;
          return;
        }
      }
    } finally {
      await reader.cancel().catch(() => undefined);
      reader.releaseLock();
    }
  }

  private backoff(retry: number): number {
    return Math.min(maxBackoffMs, 250 * 2 ** retry * (0.75 + this.random() * 0.5));
  }

  private async pause(milliseconds: number, signal?: AbortSignal): Promise<void> {
    if (signal?.aborted) throw signal.reason;
    if (milliseconds > 0) await this.delay(milliseconds, signal);
  }
}

/** The longest server-requested wait the client sleeps through; a longer one ends the call with `retryAfterMs` set. */
const maxServerRetryAfterMs = 60_000;
const maxBackoffMs = 30_000;

/** A 429 without Retry-After: FinCopy's limit is per minute, so wait whole seconds (1, 2, 4 …, at most 30). */
function rateLimitBackoff(retry: number): number {
  return Math.min(maxBackoffMs, Math.max(1_000, 1_000 * 2 ** retry));
}

/** Retry-After in seconds or as an HTTP date; empty, malformed or past values read as "not given". */
function retryAfterMsOf(header: string | null): number | null {
  const value = header?.trim();
  if (!value) return null;
  if (/^\d+$/.test(value)) return Number(value) * 1000;
  const at = Date.parse(value);
  if (Number.isNaN(at)) return null;
  const wait = at - Date.now();
  return wait > 0 ? wait : null;
}

function errorsOf(errors: unknown): string[] {
  return Array.isArray(errors) ? errors.map((error) => (typeof error === "string" ? error : JSON.stringify(error))) : [];
}

/** A caller's key is checked here (1–120 visible characters) so a bad one fails clearly instead of as a header error. */
function idempotencyKeyOf(supplied: string | undefined): string {
  const key = supplied?.trim();
  if (!key) return randomUUID();
  if (key.length > 120 || /[\u0000-\u001f\u007f]/.test(key)) {
    throw new TypeError("idempotencyKey must be 1-120 characters without control characters.");
  }
  return key;
}
