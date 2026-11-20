import { FinApiError } from "./api-error.js";
import { FinSseParser, type FinStreamEvent } from "./sse.js";
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

/** How one Fin service is reached: its name in messages, its key header, its key and its address. */
export interface FinServiceConfig {
  /** The service's name in error messages, such as "FinGraph". */
  label: string;
  /** The request header that carries the API key. */
  keyHeader: string;
  /** The API key, when there is one. */
  apiKey?: string;
  /** Absolute API root. */
  baseUrl: string;
  /**
   * When a key is required: "construct" refuses a transport built without one, "use" lets it be built and fails the
   * first request instead, and "never" sends requests without a key (a caller's own fetch authenticates them).
   */
  requireKey: "construct" | "use" | "never";
  /** What to tell the caller to set when the key is missing, such as "the graph block's apiKey". */
  keyHint?: string;
}

/** The query string and JSON body of one request to FinGraph, FinNews or FinSocial. */
export interface FinCallInput {
  /** Named query fields. Missing and null values are left out; dates are sent as ISO 8601. */
  query?: Readonly<Record<string, unknown>>;
  /** A JSON body. */
  body?: unknown;
}

interface Reply { status: number; ok: boolean; raw: string; serverWait: number | null }

const retryableStatuses = new Set([429, 502, 503, 504]);
const writeMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/** Shared authenticated transport. Resource methods provide the route and generated data type. */
export class FinHttpTransport {
  private readonly service: FinServiceConfig;
  private readonly apiKey: string;
  private readonly baseUrl: string;
  private readonly credentials: "omit" | "same-origin" | "include";
  private readonly timeoutMs: number;
  private readonly maxRetries: number;
  private readonly fetcher: typeof fetch;
  private readonly delay: (milliseconds: number, signal?: AbortSignal) => Promise<void>;
  private readonly random: () => number;

  /** Creates the transport for FinCopy, or for another service when its configuration is given. */
  constructor(options: FinClientOptions, service?: FinServiceConfig) {
    this.service = service ?? {
      label: "FinCopy",
      keyHeader: "X-Api-Key",
      apiKey: options.apiKey,
      baseUrl: options.baseUrl ?? "https://api.fincopy.io",
      requireKey: options.fetch ? "never" : "construct",
      keyHint: "the apiKey option",
    };
    this.apiKey = this.service.apiKey?.trim() ?? "";
    if (!this.apiKey && this.service.requireKey === "construct") {
      throw new TypeError("apiKey is required unless a custom fetch authenticates the member session.");
    }
    this.baseUrl = this.service.baseUrl.replace(/\/+$/, "");
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
    const label = this.service.label;
    const reply = await this.exchange(method, path, body, requestOptions);
    const { status, raw, serverWait } = reply;
    let envelope: FinEnvelope<T> | null = null;
    try { envelope = raw ? JSON.parse(raw) as FinEnvelope<T> : null; } catch { /* mapped to a typed API error below */ }
    if (!reply.ok || !envelope || envelope.isOk !== true) {
      const fallback = `The ${label} API request failed with HTTP ${status}.`;
      const message = typeof envelope?.message === "string" && envelope.message ? envelope.message : fallback;
      const errorCode = status === 429 ? "rate_limited" : reply.ok ? "invalid_response" : null;
      throw new FinApiError(status, message, envelope?.code ?? null, errorsOf(envelope?.errors),
        { errorCode, retryAfterMs: serverWait });
    }
    if (!Object.prototype.hasOwnProperty.call(envelope, "data") || envelope.data === null) {
      throw new FinApiError(status, `The ${label} API returned an empty response.`, envelope.code ?? null,
        errorsOf(envelope.errors), { errorCode: "empty_response" });
    }
    return envelope;
  }

  /**
   * Sends one request to FinGraph, FinNews or FinSocial and returns the parsed JSON reply as it came, typed as the
   * operation's documented 200 response. Refusals (401, 402, 403, 429 and the rest) become a {@link FinApiError}.
   */
  async call<T>(method: string, path: string, input: FinCallInput = {}, requestOptions: FinRequestOptions = {}): Promise<T> {
    const label = this.service.label;
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(input.query ?? {})) {
      if (value === undefined || value === null) continue;
      params.append(key, value instanceof Date ? value.toISOString() : String(value));
    }
    const suffix = params.toString();
    const reply = await this.exchange(method, suffix ? `${path}?${suffix}` : path, input.body, requestOptions);
    const { status, raw, serverWait } = reply;
    let parsed: unknown;
    try { parsed = raw ? JSON.parse(raw) : undefined; } catch { parsed = undefined; }
    const object = parsed !== null && typeof parsed === "object" && !Array.isArray(parsed) ? parsed as Record<string, unknown> : null;
    if (!reply.ok) throw refusal(label, status, object, serverWait);
    if (!object) {
      throw new FinApiError(status, `The ${label} API returned an ${raw ? "invalid" : "empty"} response.`, null, [],
        { errorCode: raw ? "invalid_response" : "empty_response" });
    }
    // The /api routes wrap their data in an envelope that can say it failed even on HTTP 200.
    if (object.isOk === false) throw refusal(label, status, object, serverWait);
    return object as T;
  }

  /** Sends one request with the retry rules shared by every service and returns the last reply. */
  private async exchange(method: string, path: string, body: unknown, requestOptions: FinRequestOptions): Promise<Reply> {
    const label = this.service.label;
    const verb = method.toUpperCase();
    const writes = writeMethods.has(verb);
    if (!this.apiKey && this.service.requireKey !== "never") {
      throw new TypeError(`${label} needs an API key. Set ${this.service.keyHint ?? "its apiKey"} when you create the FinClient.`);
    }
    // Unset fields are left out rather than sent as null; the API reads a missing field and a null one the same way.
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
      const timer = setTimeout(() => { timedOut = true; controller.abort(new Error(`${label} request timed out.`)); }, this.timeoutMs);
      let response: Response;
      let raw: string;
      try {
        const headers = new Headers({ Accept: "application/json" });
        if (this.apiKey) headers.set(this.service.keyHeader, this.apiKey);
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
            ? new FinApiError(0, `The ${label} API did not answer in time.`, null, [], { errorCode: "timeout", cause: error })
            : new FinApiError(0, `The ${label} API could not be reached.`, null, [], { errorCode: "network", cause: error });
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
      return { status: response.status, ok: response.ok, raw, serverWait };
    }
  }

  async requestPage<T>(method: string, path: string, requestOptions: FinRequestOptions = {}): Promise<FinPage<T>> {
    const envelope = await this.sendEnvelope<T[]>(method, path, undefined, requestOptions);
    if (!Array.isArray(envelope.data)) {
      throw new FinApiError(200, `The ${this.service.label} API returned an invalid page.`, envelope.code ?? null, envelope.errors ?? []);
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

  /** Reads the event stream until the server closes it or the caller aborts. It does not reconnect. */
  async *requestEvents(path: string, requestOptions: FinRequestOptions = {}): AsyncGenerator<FinStreamEvent, void, undefined> {
    const url = new URL(path.replace(/^\//, ""), `${this.baseUrl}/`);
    const headers = new Headers({ Accept: "text/event-stream" });
    if (this.apiKey) headers.set(this.service.keyHeader, this.apiKey);
    let response: Response;
    try {
      response = await this.fetcher(url, {
        method: "GET", headers, signal: requestOptions.signal, credentials: this.credentials,
      });
    } catch (error) {
      if (requestOptions.signal?.aborted) throw error;
      throw new FinApiError(0, "The FinCopy event stream could not be opened.", null, [], { errorCode: "network", cause: error });
    }
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
    const parser = new FinSseParser();
    let buffer = "";
    try {
      while (true) {
        if (requestOptions.signal?.aborted) throw requestOptions.signal.reason;
        let next: Awaited<ReturnType<typeof reader.read>>;
        try {
          next = await reader.read();
        } catch (error) {
          // The caller's own cancellation is not wrapped; a dropped connection is a network error.
          if (requestOptions.signal?.aborted) throw error;
          throw new FinApiError(0, "The FinCopy event stream was interrupted.", null, [], { errorCode: "network", cause: error });
        }
        buffer += decoder.decode(next.value, { stream: !next.done });
        const lines = buffer.split(/\r?\n/);
        buffer = lines.pop() ?? "";
        for (const line of lines) {
          const item = parser.add(line);
          if (item) yield item;
        }
        if (next.done) {
          if (buffer) { const item = parser.add(buffer); if (item) yield item; }
          const final = parser.complete();
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
  if (!key) return newIdempotencyKey();
  if (key.length > 120 || /[\u0000-\u001f\u007f]/.test(key)) {
    throw new TypeError("idempotencyKey must be 1-120 characters without control characters.");
  }
  return key;
}

interface WebCryptoLike {
  randomUUID?: () => string;
  getRandomValues?: (array: Uint8Array) => Uint8Array;
}

/**
 * A fresh key for one write. Browsers, Deno, Bun and Node 19+ have Web Crypto as a global; Node 18 does not without a
 * flag, so the key then comes from Math.random (it only has to be unique, not secret).
 */
function newIdempotencyKey(): string {
  const webCrypto = (globalThis as { crypto?: WebCryptoLike }).crypto;
  if (typeof webCrypto?.randomUUID === "function") return webCrypto.randomUUID();
  const bytes = new Uint8Array(16);
  if (typeof webCrypto?.getRandomValues === "function") webCrypto.getRandomValues(bytes);
  else for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** Reads a refusal from either reply shape: FinApiError (`{ error, message, lane, limit, catag, tier }`), the /v1 error body (`{ error: { code, message } }`) or the /api envelope. */
function refusal(label: string, status: number, body: Record<string, unknown> | null, retryAfterMs: number | null): FinApiError {
  const text = (value: unknown): string | null => (typeof value === "string" && value ? value : typeof value === "number" ? String(value) : null);
  const nested = body?.error !== null && typeof body?.error === "object" ? body.error as Record<string, unknown> : null;
  const error = text(body?.error) ?? text(nested?.code) ?? text(nested?.Code);
  const message = text(body?.message) ?? text(nested?.message) ?? text(nested?.Message) ?? `The ${label} API request failed with HTTP ${status}.`;
  const code = typeof body?.code === "number" ? body.code : null;
  return new FinApiError(status, message, code, errorsOf(body?.errors), {
    errorCode: status === 429 ? "rate_limited" : null,
    retryAfterMs,
    error,
    lane: text(body?.lane),
    limit: body?.limit,
    catag: text(body?.catag),
    tier: text(body?.tier),
  });
}
