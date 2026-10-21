import { ChangesResource, AccountsResource, OrdersResource, WebhooksResource, FxResource } from "./resources.js";
import { FinHttpTransport } from "./http-transport.js";
import type { FinClientOptions } from "./options.js";

/** Public FinCopy API client. Keep API keys in trusted server-side code. */
export class FinClient {
  readonly copy: {
    readonly accounts: AccountsResource;
    readonly orders: OrdersResource;
    readonly fx: FxResource;
    readonly changes: ChangesResource;
    readonly webhooks: WebhooksResource;
  };

  constructor(options: FinClientOptions) {
    const transport = new FinHttpTransport(options);
    this.copy = {
      accounts: new AccountsResource(transport),
      orders: new OrdersResource(transport),
      fx: new FxResource(transport),
      changes: new ChangesResource(transport),
      webhooks: new WebhooksResource(transport),
    };
  }
}
