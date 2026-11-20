"""The :class:`FinClient` entry point and its options."""
from __future__ import annotations

import random as _random
import time
from dataclasses import dataclass, field
from typing import Callable, Optional
from urllib.parse import urlsplit

from ._generated.graph_ops import FinGraph
from ._generated.news_ops import FinNews
from ._generated.social_ops import FinSocial
from ._service import Service
from ._transport import Transport, UrllibTransport
from .copy import FinCopy

__all__ = ["FinClient", "ServiceOptions", "DEFAULT_URLS"]

DEFAULT_URLS = {
    "copy": "https://api.fincopy.io",
    "graph": "https://x.fingraph.io",
    "news": "https://x.finnews.io",
    "social": "https://x.finsocial.io",
}
DEFAULT_TIMEOUT = 30.0
DEFAULT_MAX_RETRIES = 3


@dataclass(frozen=True)
class ServiceOptions:
    """Settings of one part of the client. Anything left ``None`` takes the client's own value or the default."""

    api_key: Optional[str] = field(default=None, repr=False)
    base_url: Optional[str] = None
    timeout: Optional[float] = None
    max_retries: Optional[int] = None


def _checked_url(name: str, url: str) -> str:
    parts = urlsplit(url)
    if parts.scheme not in ("http", "https") or not parts.netloc or parts.query or parts.fragment:
        raise ValueError(f"{name} base_url must be an absolute HTTP or HTTPS URL without a query or fragment.")
    return url.rstrip("/")


class FinClient:
    """One client for FinCopy, FinGraph, FinNews and FinSocial.

    ``api_key`` and ``base_url`` belong to FinCopy (``fin.copy``). The other three parts take their own
    :class:`ServiceOptions`; their keys travel in the ``FinUsrKey`` header. A part used without its key raises
    :class:`~finclient.MissingApiKeyError` when it is first used, so a client may be built with only the keys it needs.

    Example:
        >>> fin = FinClient(api_key="fck_...", news=ServiceOptions(api_key="..."))
        >>> fin.copy.accounts.list()                      # doctest: +SKIP
        >>> fin.news.v1.news.all(pageSize=20)             # doctest: +SKIP
    """

    copy: FinCopy
    graph: FinGraph
    news: FinNews
    social: FinSocial

    def __init__(
        self,
        api_key: Optional[str] = None,
        base_url: Optional[str] = None,
        *,
        graph: Optional[ServiceOptions] = None,
        news: Optional[ServiceOptions] = None,
        social: Optional[ServiceOptions] = None,
        timeout: float = DEFAULT_TIMEOUT,
        max_retries: int = DEFAULT_MAX_RETRIES,
        transport: Optional[Transport] = None,
        sleep: Callable[[float], None] = time.sleep,
        rand: Callable[[], float] = _random.random,
    ) -> None:
        if not timeout > 0:
            raise ValueError("timeout must be positive.")
        if isinstance(max_retries, bool) or not isinstance(max_retries, int) or max_retries < 0:
            raise ValueError("max_retries cannot be negative.")
        shared = transport if transport is not None else UrllibTransport()

        def build(
            key: str, name: str, header: str, options: ServiceOptions, hint: str, strict: bool = False
        ) -> Service:
            return Service(
                name=name,
                base_url=_checked_url(name, options.base_url or DEFAULT_URLS[key]),
                api_key=options.api_key,
                key_header=header,
                transport=shared,
                timeout=options.timeout if options.timeout is not None else timeout,
                max_retries=options.max_retries if options.max_retries is not None else max_retries,
                sleep=sleep,
                rand=rand,
                strict=strict,
                key_hint=hint,
            )

        self.copy = FinCopy(build(
            "copy", "FinCopy", "X-Api-Key", ServiceOptions(api_key=api_key, base_url=base_url),
            "Pass api_key=... to FinClient.", strict=True))
        self.graph = FinGraph(build(
            "graph", "FinGraph", "FinUsrKey", graph or ServiceOptions(),
            "Pass graph=ServiceOptions(api_key=...) to FinClient."))
        self.news = FinNews(build(
            "news", "FinNews", "FinUsrKey", news or ServiceOptions(),
            "Pass news=ServiceOptions(api_key=...) to FinClient."))
        self.social = FinSocial(build(
            "social", "FinSocial", "FinUsrKey", social or ServiceOptions(),
            "Pass social=ServiceOptions(api_key=...) to FinClient."))
