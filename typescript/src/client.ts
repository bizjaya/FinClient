import { ChangesResource, AccountsResource, OrdersResource, WebhooksResource, FxResource } from "./resources.js";
import { FinHttpTransport, type FinServiceConfig } from "./http-transport.js";
import { FinGraph } from "./graph.generated.js";
import { FinNews } from "./news.generated.js";
import { FinSocial } from "./social.generated.js";
import type { FinClientOptions, FinServiceOptions } from "./options.js";

/**
 * Public Fin API client: `copy` (FinCopy), `graph` (FinGraph), `news` (FinNews) and `social` (FinSocial). Keep API keys in
 * trusted server-side code. A service whose key was not given throws a clear error when you first call it.
 */
export class FinClient {
  readonly copy: {
    readonly accounts: AccountsResource;
    readonly orders: OrdersResource;
    readonly fx: FxResource;
    readonly changes: ChangesResource;
    readonly webhooks: WebhooksResource;
  };
  /** FinGraph: market data, tickers, signals and statistics. */
  readonly graph: FinGraph;
  /** FinNews: news, sentiment, trending, sources and videos. */
  readonly news: FinNews;
  /** FinSocial: prediction markets, social posts, authors and videos. */
  readonly social: FinSocial;

  constructor(options: FinClientOptions) {
    const hasService = Boolean(options.graph || options.news || options.social);
    // With a service block given, FinCopy's key may be absent: using FinCopy then fails at first use instead.
    const copy = hasService && !options.apiKey?.trim() && !options.fetch
      ? new FinHttpTransport(options, {
        label: "FinCopy", keyHeader: "X-Api-Key", baseUrl: options.baseUrl ?? "https://api.fincopy.io",
        requireKey: "use", keyHint: "the apiKey option",
      })
      : new FinHttpTransport(options);
    this.copy = {
      accounts: new AccountsResource(copy),
      orders: new OrdersResource(copy),
      fx: new FxResource(copy),
      changes: new ChangesResource(copy),
      webhooks: new WebhooksResource(copy),
    };
    this.graph = new FinGraph(service(options, "FinGraph", "graph", options.graph, "https://x.fingraph.io"));
    this.news = new FinNews(service(options, "FinNews", "news", options.news, "https://x.finnews.io"));
    this.social = new FinSocial(service(options, "FinSocial", "social", options.social, "https://x.finsocial.io"));
  }
}

/** FinGraph, FinNews and FinSocial take the key in the FinUsrKey header; a missing key fails the first call, not the constructor. */
function service(options: FinClientOptions, label: string, block: string, own: FinServiceOptions | undefined, defaultBaseUrl: string): FinHttpTransport {
  const config: FinServiceConfig = {
    label,
    keyHeader: "FinUsrKey",
    apiKey: own?.apiKey,
    baseUrl: own?.baseUrl ?? defaultBaseUrl,
    requireKey: "use",
    keyHint: `${block}: { apiKey }`,
  };
  return new FinHttpTransport(options, config);
}
