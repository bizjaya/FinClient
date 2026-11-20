"""FinClient: one client for the FinCopy, FinGraph, FinNews and FinSocial APIs."""
from __future__ import annotations

from ._errors import (
    ApiConnectionError,
    ApiResponseError,
    AuthenticationError,
    FinApiError,
    InsufficientCreditsError,
    MissingApiKeyError,
    NotFoundError,
    PermissionDeniedError,
    RateLimitError,
    ServerError,
)
from ._paging import Page, PageInfo
from ._transport import HttpRequest, HttpResponse, StreamResponse, Transport, TransportError, UrllibTransport
from ._version import __version__
from .client import FinClient, ServiceOptions
from .copy import FinWebhook, StreamEvent, verify_signature

__all__ = [
    "ApiConnectionError",
    "ApiResponseError",
    "AuthenticationError",
    "FinApiError",
    "FinClient",
    "FinWebhook",
    "HttpRequest",
    "HttpResponse",
    "InsufficientCreditsError",
    "MissingApiKeyError",
    "NotFoundError",
    "Page",
    "PageInfo",
    "PermissionDeniedError",
    "RateLimitError",
    "ServerError",
    "ServiceOptions",
    "StreamEvent",
    "StreamResponse",
    "Transport",
    "TransportError",
    "UrllibTransport",
    "__version__",
    "verify_signature",
]
