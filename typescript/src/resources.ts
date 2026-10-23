import { readAllChanges, readChangePages, type FinChangeFeedPage } from "./change-feed.js";
import type { FinRequestOptions } from "./options.js";
import { FinHttpTransport, type FinPage, type FinPageInfo } from "./http-transport.js";
import type { FinStreamEvent } from "./sse.js";
import type { FinCopyDto, FinCopySchemas, FinResponseData } from "./types.js";

type D<K extends keyof FinCopySchemas> = FinResponseData<K>;
type CopyDto<K extends keyof FinCopySchemas> = FinCopyDto<K>;

/** Paging and search inputs for FinCopy lists. */
export interface FinPageQuery {
  PageNo?: number; PageSize?: number; Search?: string;
  SortBy?: readonly (readonly string[])[]; SrchBy?: readonly (readonly string[])[]; Include?: readonly string[];
}

/** Filters supported by cross-account FXS lists. */
export interface FinFxsListQuery {
  page?: FinPageQuery; acno?: number; prno?: number; demo?: boolean; platform?: string; search?: string;
}

export interface FinPerformanceQuery extends FinRequestOptions { fromDt?: string | Date; toDt?: string | Date }

/** The cross-account performance reading: every reachable account, or one with `acno`; `demo` picks demo or real. */
export interface FinFxsPerformanceQuery extends FinPerformanceQuery { acno?: number; demo?: boolean }

function pageQuery(query: FinPageQuery = {}): string {
  const params = new URLSearchParams();
  for (const key of ["PageNo", "PageSize"] as const) {
    const value = query[key];
    if (value !== undefined) params.append(key, String(value));
  }
  if (query.Search !== undefined) params.append("search", query.Search);
  for (const key of ["SortBy", "SrchBy"] as const)
    (query[key] ?? []).forEach((clause, index) => clause.forEach((value, part) => params.append(`${key}[${index}][${part}]`, value)));
  for (const value of query.Include ?? []) params.append("Include", value);
  return params.toString();
}

function queryPath(path: string, page?: FinPageQuery, filters: Readonly<Record<string, string | number | boolean | null | undefined>> = {}): string {
  const params = new URLSearchParams(pageQuery(page));
  for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== null) params.set(key, String(value));
  const suffix = params.toString();
  return suffix ? `${path}?${suffix}` : path;
}

function accountPath(acno: number): string {
  if (!Number.isSafeInteger(acno) || acno <= 0) throw new RangeError("acno must be a positive safe integer.");
  return `v1/fxs/accounts/${acno}`;
}

function numericId(value: number, name: string, allowZero = true): string {
  if (!Number.isSafeInteger(value) || value < (allowZero ? 0 : 1)) throw new RangeError(`${name} is out of range.`);
  return String(value);
}

function dateValue(value: string | Date): string { return value instanceof Date ? value.toISOString() : value; }
function dateFilters(query: FinPerformanceQuery): Record<string, string | undefined> {
  return {
    fromDt: query.fromDt === undefined ? undefined : dateValue(query.fromDt),
    toDt: query.toDt === undefined ? undefined : dateValue(query.toDt),
  };
}

/** Account lifecycle, settings, symbols, positions and results. */
export class AccountsResource {
  constructor(private readonly http: FinHttpTransport) {}

  /** Lists reachable accounts, optionally filtered by external reference. */
  list(page: FinPageQuery = {}, externalRef?: string, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoMemberAccount">>> {
    return this.http.requestPage("GET", queryPath("v1/fxs/accounts", page, { externalRef }), options);
  }
  /** Adds a hosted account. */
  make(command: CopyDto<"CmdMemberAccountMake">, options: FinRequestOptions = {}): Promise<D<"DtoFleetAccountResPonse">> {
    return this.http.request("POST", "v1/fxs/accounts", command, options);
  }
  /** Creates a demo account. */
  makeDemo(command: CopyDto<"CmdMemberDemoMake">, options: FinRequestOptions = {}): Promise<D<"DtoFleetAccountResPonse">> {
    return this.http.request("POST", "v1/fxs/accounts/demo", command, options);
  }
  /** Reads demo availability for one platform. */
  demoAvailability(platform?: string, options: FinRequestOptions = {}): Promise<D<"DtoDemoAvailabilityResPonse">> {
    return this.http.request("GET", queryPath("v1/fxs/accounts/demo-availability", undefined, { platform }), undefined, options);
  }
  /** Reads one account by ACNO. */
  get(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoMemberAccountResPonse">> {
    return this.http.request("GET", accountPath(acno), undefined, options);
  }
  /** Sets or clears the account's external reference. */
  setExternalRef(acno: number, command: CopyDto<"CmdTermAccountExternalRef">, options: FinRequestOptions = {}): Promise<D<"DtoTermAccountResPonse">> {
    return this.http.request("PUT", `${accountPath(acno)}/external-ref`, command, options);
  }
  /** Reads account settings. */
  getSettings(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsAcctResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/settings`, undefined, options);
  }
  /** Changes account settings. */
  patchSettings(acno: number, command: CopyDto<"CmdApiFxsAcctPatch">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAcctResPonse">> {
    return this.http.request("PATCH", `${accountPath(acno)}/settings`, command, options);
  }
  /** Lists pairs for one account. */
  pairs(acno: number, page: FinPageQuery = {}, search?: string, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsPair">>> {
    return this.http.requestPage("GET", queryPath(`${accountPath(acno)}/pairs`, page, { search }), options);
  }
  /** Reads one pair by PRNO. */
  pair(acno: number, prno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsPairResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/pairs/${numericId(prno, "prno")}`, undefined, options);
  }
  /** Changes one pair's settings. */
  patchPair(acno: number, prno: number, command: CopyDto<"CmdApiFxsPairPatch">, options: FinRequestOptions = {}): Promise<D<"DtoFxsPairResPonse">> {
    return this.http.request("PATCH", `${accountPath(acno)}/pairs/${numericId(prno, "prno")}`, command, options);
  }
  /** Reads the broker symbols and enabled list. */
  symbols(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsSymbolCatalogResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/symbols`, undefined, options);
  }
  /** Adds or removes enabled symbols. */
  editSymbols(acno: number, command: CopyDto<"CmdFxsSymbolsEdit">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("PUT", `${accountPath(acno)}/symbols`, command, options);
  }
  /** Reads a symbol's broker specification and latest quote. */
  quote(acno: number, symbol: string, options: FinRequestOptions = {}): Promise<D<"DtoFxsSymbolSpecResPonse">> {
    if (!symbol.trim()) throw new TypeError("symbol is required.");
    return this.http.request("GET", `${accountPath(acno)}/quotes/${encodeURIComponent(symbol)}`, undefined, options);
  }
  /** Lists live orders for one account. */
  orders(acno: number, page: FinPageQuery = {}, search?: string, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdr">>> {
    return this.http.requestPage("GET", queryPath(`${accountPath(acno)}/orders`, page, { search }), options);
  }
  /** Reads one live or settled order. */
  order(acno: number, oid: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/orders/${numericId(oid, "oid")}`, undefined, options);
  }
  /** Lists settled orders. */
  history(acno: number, page: FinPageQuery = {}, search?: string, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdh">>> {
    return this.http.requestPage("GET", queryPath(`${accountPath(acno)}/history`, page, { search }), options);
  }
  /** Reads one settled order by OID. */
  historyItem(acno: number, oid: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdhResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/history/${numericId(oid, "oid")}`, undefined, options);
  }
  /** Lists HYBRID DCA proposals waiting for an answer. */
  dcaQueue(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrIReadOnlyListResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/dca`, undefined, options);
  }
  /** Lists daily performance rows. */
  performance(acno: number, query: FinPerformanceQuery = {}): Promise<D<"DtoFxsPerfIReadOnlyListResPonse">> {
    return this.http.request("GET", queryPath(`${accountPath(acno)}/performance`, undefined, dateFilters(query)), undefined, query);
  }
  /** Reads the account's performance window. */
  performanceWindow(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsPerfWindowResPonse">> {
    return this.http.request("GET", `${accountPath(acno)}/performance/window`, undefined, options);
  }
  /** Reads performance bars for an optional UTC date range. */
  performanceBars(acno: number, query: FinPerformanceQuery = {}): Promise<D<"DtoFxsPerfBarsResPonse">> {
    return this.http.request("GET", queryPath(`${accountPath(acno)}/performance/bars`, undefined, dateFilters(query)), undefined, query);
  }
  /** Lists paged account activity. */
  activity(acno: number, page: FinPageQuery = {}, search?: string, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoMemberActivity">>> {
    return this.http.requestPage("GET", queryPath(`${accountPath(acno)}/activity`, page, { search }), options);
  }
  /** Previews applying a strategy (the account owner's or a FinCopy preset): what each symbol would change. Nothing changes. */
  previewStrategy(acno: number, command: CopyDto<"CmdApiFxsStgApply">, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgCascadePreviewResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/strategy/preview`, command, options);
  }
  /** Applies a strategy to the account and its symbols; a side holding orders keeps what it must keep. */
  applyStrategy(acno: number, command: CopyDto<"CmdApiFxsStgApply">, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgCascadeApplyResultResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/strategy/apply`, command, options);
  }
  /** Lists recent terminal operations. */
  operations(acno: number, page: FinPageQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoApiTermOperation">>> {
    return this.http.requestPage("GET", queryPath(`${accountPath(acno)}/operations`, page), options);
  }
  /** Queues a broker-password change. */
  changePassword(acno: number, command: CopyDto<"CmdTermAccountPassword">, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/password`, command, options);
  }
  /** Queues a terminal start. */
  start(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> { return this.operation(acno, "start", options); }
  /** Queues a terminal stop. */
  stop(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> { return this.operation(acno, "stop", options); }
  /** Queues a terminal restart. */
  restart(acno: number, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> { return this.operation(acno, "restart", options); }
  /** Disconnects the hosted terminal. Pass TrPin for a session account that requires its trading PIN. */
  remove(acno: number, options?: FinRequestOptions): Promise<D<"DtoOperationQueuedResPonse">>;
  remove(acno: number, command: CopyDto<"CmdMemTerminalDisconnect">, options?: FinRequestOptions): Promise<D<"DtoOperationQueuedResPonse">>;
  remove(acno: number, commandOrOptions: CopyDto<"CmdMemTerminalDisconnect"> | FinRequestOptions = {}, options: FinRequestOptions = {}): Promise<D<"DtoOperationQueuedResPonse">> {
    const hasCommand = Object.prototype.hasOwnProperty.call(commandOrOptions, "TrPin");
    const command = hasCommand ? commandOrOptions as CopyDto<"CmdMemTerminalDisconnect"> : { TrPin: null };
    const requestOptions = hasCommand ? options : commandOrOptions as FinRequestOptions;
    return this.http.request("DELETE", accountPath(acno), command, requestOptions);
  }
  private operation(acno: number, action: string, options: FinRequestOptions): Promise<D<"DtoOperationQueuedResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/${action}`, undefined, options);
  }
}

/** Account-level trading actions. Successful writes report that work is queued, not filled. */
export class OrdersResource {
  constructor(private readonly http: FinHttpTransport) {}
  /** Preflights a trade request. */
  preInfo(acno: number, command: CopyDto<"CmdApiFxsPreInfo">, options: FinRequestOptions = {}): Promise<D<"DtoFxsPreInfoResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/trade/preinfo`, command, options);
  }
  /** Sends one trade action. */
  make(acno: number, command: CopyDto<"CmdApiFxsOrdrMake">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/trade`, command, options);
  }
  /** Sends paired BUY and SELL actions. */
  hedge(acno: number, command: CopyDto<"CmdApiFxsHedgeMake">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/trade/hedge`, command, options);
  }
  /** Modifies a trade or pending order. */
  modify(acno: number, command: CopyDto<"CmdFxsModify">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/trade/modify`, command, options);
  }
  /** Cancels an order that has not reached the terminal. */
  cancelWaiting(acno: number, command: CopyDto<"CmdFxsCancelWaiting">, options: FinRequestOptions = {}): Promise<D<"DtoFxsAccountStateResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/trade/cancel`, command, options);
  }
  /** Accepts or rejects one DCA proposal. */
  answerDca(acno: number, oid: number, command: CopyDto<"CmdFxsDcaAnswer">, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrResPonse">> {
    return this.http.request("POST", `${accountPath(acno)}/dca/${numericId(oid, "oid")}`, command, options);
  }
}

/** Cross-account FXS lists and lane-wide member resources. */
export class FxResource {
  constructor(private readonly http: FinHttpTransport) {}
  /** Lists pairs across reachable accounts. */
  pairs(query: FinFxsListQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsPair">>> {
    return this.http.requestPage("GET", listPath("v1/fxs/pairs", query), options);
  }
  /** Lists open orders across reachable accounts. */
  orders(query: FinFxsListQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdr">>> {
    return this.http.requestPage("GET", listPath("v1/fxs/orders", query), options);
  }
  /** Lists settled orders across reachable accounts. */
  history(query: FinFxsListQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoFxsOrdh">>> {
    return this.http.requestPage("GET", listPath("v1/fxs/history", query), options);
  }
  /** Lists activity across reachable accounts. */
  activity(query: FinFxsListQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoMemberActivity">>> {
    return this.http.requestPage("GET", listPath("v1/fxs/activity", query), options);
  }
  /** HYBRID DCA proposals waiting for an answer across reachable accounts, newest first. */
  dca(acno?: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsOrdrIReadOnlyListResPonse">> {
    return this.http.request("GET", queryPath("v1/fxs/dca", undefined, { acno }), undefined, options);
  }
  /** The newest terminal operations across reachable accounts. */
  operations(page: FinPageQuery = {}, acno?: number, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoApiTermOperation">>> {
    return this.http.requestPage("GET", queryPath("v1/fxs/operations", page, { acno }), options);
  }
  /** The last 30 days by currency book across reachable accounts. */
  performanceWindow(query: FinFxsPerformanceQuery = {}): Promise<D<"DtoFxsPerfWindowResPonse">> {
    return this.http.request("GET", queryPath("v1/fxs/performance/window", undefined, { acno: query.acno, demo: query.demo }), undefined, query);
  }
  /** Daily performance bars across reachable accounts for an optional UTC date range. */
  performanceBars(query: FinFxsPerformanceQuery = {}): Promise<D<"DtoFxsPerfBarsResPonse">> {
    return this.http.request("GET", queryPath("v1/fxs/performance/bars", undefined,
      { acno: query.acno, demo: query.demo, ...dateFilters(query) }), undefined, query);
  }
  /** Lists the strategies you can apply: your own and FinCopy's presets; with `acno`, that account owner's. */
  strategies(acno?: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgIReadOnlyListResPonse">> {
    return this.http.request("GET", queryPath("v1/fxs/strategies", undefined, { acno }), undefined, options);
  }
  /** Reads one strategy. */
  strategy(isno: number, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgResPonse">> {
    return this.http.request("GET", strategyPath(isno), undefined, options);
  }
  /** Creates one of your own strategies. */
  makeStrategy(command: CopyDto<"CmdFxsStgMake">, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgResPonse">> {
    return this.http.request("POST", "v1/fxs/strategies", command, options);
  }
  /** Changes one of your own strategies; fields left unset keep their values. */
  patchStrategy(isno: number, command: CopyDto<"CmdFxsStgPatch">, options: FinRequestOptions = {}): Promise<D<"DtoFxsStgResPonse">> {
    return this.http.request("PATCH", strategyPath(isno), command, options);
  }
  /** Deletes one of your own strategies. */
  deleteStrategy(isno: number, options: FinRequestOptions = {}): Promise<D<"BooleanResPonse">> {
    return this.http.request("DELETE", strategyPath(isno), undefined, options);
  }
  /** Reads the signals shown for enabled symbols. */
  signals(options: FinRequestOptions = {}): Promise<D<"DtoShelfSignalsResPonse">> {
    return this.http.request("GET", "v1/fxs/signals", undefined, options);
  }
  /** Lists warning and error activity. */
  alerts(page: FinPageQuery = {}, options: FinRequestOptions = {}): Promise<FinPage<CopyDto<"DtoMemberActivity">>> {
    return this.http.requestPage("GET", queryPath("v1/fxs/alerts", page), options);
  }
  /**
   * Opens the live event stream for reachable accounts. It does not reconnect: it ends when the server closes it (about
   * every 30 minutes) or the connection breaks, so open it again in a loop, waiting a few seconds between attempts.
   */
  stream(options: FinRequestOptions = {}): AsyncGenerator<FinStreamEvent, void, undefined> {
    return this.http.requestEvents("v1/fxs/stream", options);
  }
  /** Searches broker servers available when adding an account. */
  brokers(query?: string, platform?: string, demosOnly?: boolean, options: FinRequestOptions = {}): Promise<D<"DtoBrokerServerSearchIReadOnlyListResPonse">> {
    return this.http.request("GET", queryPath("v1/brokers", undefined, { Q: query, Platform: platform, DemosOnly: demosOnly }), undefined, options);
  }
}

/** Changes feed resource. */
export class ChangesResource {
  constructor(private readonly http: FinHttpTransport) {}
  /** Reads one changes page using an optional cursor. */
  get(cursor?: string, take = 500, options: FinRequestOptions = {}): Promise<D<"DtoApiChangeFeedResPonse">> {
    if (!Number.isInteger(take) || take < 1 || take > 1000) throw new RangeError("take must be between 1 and 1000.");
    return this.http.request("GET", queryPath("v1/changes", undefined, { cursor, take }), undefined, options);
  }
  /** Reads changes page by page, following each cursor. */
  readPages(cursor?: string, take = 500, signal?: AbortSignal): AsyncGenerator<FinChangeFeedPage<CopyDto<"DtoApiChange">>, void, undefined> {
    return readChangePages((next, nextSignal) => this.page(next, take, nextSignal), cursor, signal);
  }
  /** Streams all available changes. Use readPages to retain a resume cursor. */
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
    return this.http.request("GET", "v1/webhook", undefined, options);
  }
  /** Sets the webhook destination and returns its signing secret once. */
  set(command: CopyDto<"CmdApiWebhookSet">, options: FinRequestOptions = {}): Promise<D<"DtoApiWebhookResPonse">> {
    return this.http.request("PUT", "v1/webhook", command, options);
  }
  /** Removes this key's webhook. */
  delete(options: FinRequestOptions = {}): Promise<D<"BooleanResPonse">> {
    return this.http.request("DELETE", "v1/webhook", undefined, options);
  }
  /** Sends a signed test delivery. */
  ping(options: FinRequestOptions = {}): Promise<D<"DtoApiWebhookPingResPonse">> {
    return this.http.request("POST", "v1/webhook/ping", undefined, options);
  }
}

export type { FinPageInfo, FinStreamEvent };

function strategyPath(isno: number): string { return `v1/fxs/strategies/${numericId(isno, "isno")}`; }

function listPath(path: string, query: FinFxsListQuery): string {
  return queryPath(path, query.page, { acno: query.acno, prno: query.prno, demo: query.demo, platform: query.platform, search: query.search });
}
