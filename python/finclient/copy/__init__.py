"""The FinCopy part of the client: trading accounts, orders, strategies, the change feed and webhooks."""
from __future__ import annotations

from .._service import Service
from ._common import PageQuery
from .accounts import AccountsResource
from .changes import ChangeFeedPage, ChangesResource, read_change_pages
from .fx import FxResource
from .orders import OrdersResource
from .sse import SseParser, StreamEvent
from .webhooks import FinWebhook, WebhooksResource, verify_signature

__all__ = [
    "AccountsResource",
    "ChangeFeedPage",
    "ChangesResource",
    "FinCopy",
    "FinWebhook",
    "FxResource",
    "OrdersResource",
    "PageQuery",
    "SseParser",
    "StreamEvent",
    "WebhooksResource",
    "read_change_pages",
    "verify_signature",
]


class FinCopy:
    """FinCopy: ``accounts``, ``orders``, ``fx``, ``changes`` and ``webhooks``. Keep API keys in trusted server code."""

    def __init__(self, service: Service) -> None:
        self.accounts = AccountsResource(service)
        self.orders = OrdersResource(service)
        self.fx = FxResource(service)
        self.changes = ChangesResource(service)
        self.webhooks = WebhooksResource(service)
        self.webhook = FinWebhook
