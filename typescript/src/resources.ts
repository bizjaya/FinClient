import { readAllChanges, readChangePages, type FinChangeFeedPage } from "./change-feed.js";
import type { FinRequestOptions } from "./options.js";
import { FinHttpTransport, type FinPage, type FinPageInfo } from "./http-transport.js";
import type { FinCopyDto, FinCopySchemas, FinResponseData } from "./types.js";

type D<K extends keyof FinCopySchemas> = FinResponseData<K>;
type CopyDto<K extends keyof FinCopySchemas> = FinCopyDto<K>;

/** Common FinCopy paging and sorting inputs. Arrays become repeated query keys. */
export interface FinPageQuery {
  PageNo?: number;
  PageSize?: number;
  PageOff?: number;
  IsAsc?: boolean;
  SortCol?: string;
  SortDir?: string;
  SrchCol?: string;
  SrchVal?: string;
  Search?: string;
  SortBy?: readonly string[];
  SrchBy?: readonly string[];
  Include?: readonly string[];
}

export interface FinPerformanceQuery extends FinRequestOptions {
  fromDt?: string | Date;
  toDt?: string | Date;
}

function pageQuery(query: FinPageQuery = {}): string {
  const params = new URLSearchParams();
  const scalarKeys = ["PageNo", "PageSize", "PageOff", "IsAsc", "SortCol", "SortDir", "SrchCol", "SrchVal"] as const;
  for (const key of scalarKeys) {
    const value = query[key];
    if (value !== undefined) params.append(key, String(value));
  }
  if (query.Search !== undefined) params.append("search", query.Search);
  for (const key of ["SortBy", "SrchBy", "Include"] as const) {
    for (const value of query[key] ?? []) params.append(key, value);
  }
  const value = params.toString();
  return value ? `?${value}` : "";
}

function accountPath(accountId: string): string {
  if (!accountId?.trim()) throw new TypeError("accountId is required.");
  return `v1/accounts/${encodeURIComponent(accountId)}`;
}

function numericId(value: number, name: string): string {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${name} must be a non-negative safe integer.`);
  return String(value);
}

function dateQuery(value: string | Date): string {
  return value instanceof Date ? value.toISOString() : value;
}

/** Account, symbols, settings and terminal operations. */
export class AccountsResource {
  constructor(private readonly http: FinHttpTransport) {}

  /** Lists the accounts this API key can reach. */
  list(options: FinRequestOptions = {}): Promise<D<"DtoMemberAccountIReadOnlyListResPonse">> {
    return this.http.request("GET", "v1/accounts", undefined, options);
  }

  /** Adds a hosted account for the key's owner. */
  make(command: CopyDto<"CmdMemberAccountMake">, options: FinRequestOptions = {}): Promise<D<"DtoFleetAccountResPonse">> {
    return this.http.request("POST", "v1/accounts/make", command, options);
  }

  /** Reads one account and its latest state. */
  get(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoMemberAccountResPonse">> {
    return this.http.request("GET", accountPath(accountId), undefined, options);
  }

  /** Sets or clears the account's external reference. */
  setExternalRef(accountId: string, command: CopyDto<"CmdTermAccountExternalRef">, options: FinRequestOptions = {}): Promise<D<"DtoTermAccountResPonse">> {
    return this.http.request("PUT", `${accountPath(accountId)}/external-ref`, command, options);
  }

  /** Reads the account's trading settings. */
  getTrading(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoFxsAcctResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/fxs`, undefined, options);
  }

  /** Changes the account's trading settings. */
  patchTrading(accountId: string, command: CopyDto<"CmdFxsAcctPatch">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAcctResPonse">> {
    return this.http.request("PATCH", `${accountPath(accountId)}/fxs`, command, options);
  }

  /** Lists the account's symbols and their BUY and SELL books. */
  listPairs(accountId: string, query: FinPageQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsPair">>> {
    return this.http.requestPage("GET", `${accountPath(accountId)}/pairs${pageQuery(query)}`, options);
  }

  /** Reads one symbol and its settings. */
  getPair(accountId: string, prno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsPairResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/pairs/${numericId(prno, "prno")}`, undefined, options);
  }

  /** Changes one symbol's settings. */
  patchPair(accountId: string, prno: number, command: CopyDto<"CmdFxsPairPatch">, options: FinRequestOptions = {}): Promise<D<"DtoFxsPairResPonse">> {
    return this.http.request("PATCH", `${accountPath(accountId)}/pairs/${numericId(prno, "prno")}`, command, options);
  }

  /** Lists HYBRID DCA proposals waiting for an answer. */
  dcaQueue(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrIReadOnlyListResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/dca-queue`, undefined, options);
  }

  /** Reads broker symbols and the account's enabled symbols. */
  symbols(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoFxsSymbolCatalogResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/symbols`, undefined, options);
  }

  /** Adds and removes symbols from the account's enabled list. */
  editSymbols(accountId: string, command: CopyDto<"CmdFxsSymbolsEdit">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("PUT", `${accountPath(accountId)}/symbols`, command, options);
  }

  /** Reads a symbol's broker specification and latest quote. */
  quote(accountId: string, symbol: string, options: FinRequestOptions = {}): Promise<D<"DtoFxsSymbolSpecResPonse">> {
    if (!symbol?.trim()) throw new TypeError("symbol is required.");
    return this.http.request("GET", `${accountPath(accountId)}/quotes/${encodeURIComponent(symbol)}`, undefined, options);
  }

  /** Reads daily performance rows for one account and optional UTC date range. */
  performance(accountId: string, query: FinPerformanceQuery = {}): Promise<D<"DtoFxsPerfIReadOnlyListResPonse">> {
    const params = new URLSearchParams();
    if (query.fromDt !== undefined) params.set("fromDt", dateQuery(query.fromDt));
    if (query.toDt !== undefined) params.set("toDt", dateQuery(query.toDt));
    const encoded = params.toString();
    const suffix = encoded ? `?${encoded}` : "";
    return this.http.request("GET", `${accountPath(accountId)}/performance${suffix}`, undefined, query);
  }

  /** Reads recent activity for one account. */
  activity(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoMemberActivityIReadOnlyListResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/activity`, undefined, options);
  }

  /** Queues a start operation for the hosted terminal. */
  start(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.operation(accountId, "start", options);
  }
  /** Queues a stop operation for the hosted terminal. */
  stop(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.operation(accountId, "stop", options);
  }
  /** Queues a restart operation for the hosted terminal. */
  restart(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.operation(accountId, "restart", options);
  }
  /** Queues removal of the hosted account and its terminal. */
  remove(accountId: string, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.operation(accountId, "remove", options);
  }

  private operation(accountId: string, operation: string, options: FinRequestOptions): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/${operation}`, undefined, options);
  }
}

/** Orders and trade actions. Successful writes report that work is queued, not filled. */
export class OrdersResource {
  constructor(private readonly http: FinHttpTransport) {}

  /** Lists live, queued and pending orders. */
  list(accountId: string, query: FinPageQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdr">>> {
    return this.http.requestPage("GET", `${accountPath(accountId)}/orders${pageQuery(query)}`, options);
  }
  /** Lists settled orders, newest first. */
  history(accountId: string, query: FinPageQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdh">>> {
    return this.http.requestPage("GET", `${accountPath(accountId)}/orders/history${pageQuery(query)}`, options);
  }
  /** Reads one settled order. */
  historyItem(accountId: string, oid: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdhResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/orders/history/${numericId(oid, "oid")}`, undefined, options);
  }
  /** Reads one live or settled order. */
  get(accountId: string, oid: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrResPonse">> {
    return this.http.request("GET", `${accountPath(accountId)}/orders/${numericId(oid, "oid")}`, undefined, options);
  }
  /** Sends one FXS action. A successful response means queued for the terminal, not filled. */
  make(accountId: string, command: CopyDto<"CmdFxsOrdrMake">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/orders`, command, options);
  }
  /** Accepts or rejects one HYBRID DCA proposal. */
  answerDca(accountId: string, oid: number, command: CopyDto<"CmdFxsDcaAnswer">, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/dca/${numericId(oid, "oid")}/answer`, command, options);
  }
  /** Reads the quote, limits, books and settings used by the trade ticket. */
  preInfo(accountId: string, command: CopyDto<"CmdFxsPreInfo">, options: FinRequestOptions = {}): Promise<D<"DtoFxsPreInfoResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/orders/preinfo`, command, options);
  }
  /** Sends a BUY and SELL trade together. A successful response means queued, not filled. */
  hedge(accountId: string, command: CopyDto<"CmdFxsHedgeMake">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/orders/hedge`, command, options);
  }
  /** Changes an order's TP field, SL field, expiry or pending price. */
  modify(accountId: string, command: CopyDto<"CmdFxsModify">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/trades/modify`, command, options);
  }
  /** Cancels an order that has not yet been delivered to the terminal. */
  cancelWaiting(accountId: string, command: CopyDto<"CmdFxsCancelWaiting">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(accountId)}/trades/cancel-waiting`, command, options);
  }
}

/** Change-feed resource. Read one page or stream every row while the cursor advances. */
export class ChangesResource {
  constructor(private readonly http: FinHttpTransport) {}

  /** Reads one changes page using an optional cursor. */
  get(cursor?: string, take = 500, options: FinRequestOptions = {}): Promise<D<"DtoApiChangeFeedResPonse">> {
    if (!Number.isInteger(take) || take < 1 || take > 1000) throw new RangeError("take must be between 1 and 1000.");
    const params = new URLSearchParams({ take: String(take) });
    if (cursor) params.set("cursor", cursor);
    return this.http.request("GET", `v1/accounts/changes?${params}`, undefined, options);
  }

  /**
   * Streams the available changes page by page, following each returned cursor. Save each page's `Cursor` after handling
   * its `Changes`, so a restart resumes from there.
   */
  readPages(cursor?: string, take = 500, signal?: AbortSignal): AsyncGenerator<FinChangeFeedPage<CopyDto<"DtoApiChange">>, void, undefined> {
    return readChangePages((next, nextSignal) => this.page(next, take, nextSignal), cursor, signal);
  }

  /** Streams every currently available change, following each returned cursor. Use `readPages` to save the cursor. */
  readAll(cursor?: string, take = 500, signal?: AbortSignal): AsyncGenerator<CopyDto<"DtoApiChange">, void, undefined> {
    return readAllChanges((next, nextSignal) => this.page(next, take, nextSignal), cursor, signal);
  }

  private async page(cursor: string | undefined, take: number, signal?: AbortSignal): Promise<FinChangeFeedPage<CopyDto<"DtoApiChange">>> {
    const page = await this.get(cursor, take, { signal });
    return { Changes: page.Changes ?? [], Cursor: page.Cursor ?? null, More: page.More ?? false };
  }
}

/** Webhook configuration and test delivery. */
export class WebhooksResource {
  constructor(private readonly http: FinHttpTransport) {}

  /** Reads this key's webhook configuration. */
  get(options: FinRequestOptions = {}): Promise<D<"DtoApiWebhookResPonse">> {
    return this.http.request("GET", "v1/accounts/webhook", undefined, options);
  }
  /** Sets the HTTPS destination and returns its signing secret once. */
  set(command: CopyDto<"CmdApiWebhookSet">, options: FinRequestOptions = {}): Promise<D<"DtoApiWebhookResPonse">> {
    return this.http.request("PUT", "v1/accounts/webhook", command, options);
  }
  /** Removes this key's webhook. */
  delete(options: FinRequestOptions = {}): Promise<D<"BooleanResPonse">> {
    return this.http.request("DELETE", "v1/accounts/webhook", undefined, options);
  }
  /** Sends a signed test POST to this key's webhook. */
  ping(options: FinRequestOptions = {}): Promise<D<"DtoApiWebhookPingResPonse">> {
    return this.http.request("POST", "v1/accounts/webhook/ping", undefined, options);
  }
}

export type { FinPageInfo };
