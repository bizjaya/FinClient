"""A scripted transport: tests queue responses and read back the requests the client sent."""
from __future__ import annotations

import json
from typing import Any, Callable, Dict, List, Optional, Tuple, Union
from urllib.parse import parse_qs, urlsplit

import pytest

from finclient import FinClient, HttpRequest, HttpResponse, ServiceOptions, StreamResponse

Item = Union[HttpResponse, Exception]


def reply(body: Any = None, status: int = 200, headers: Optional[Dict[str, str]] = None) -> HttpResponse:
    raw = b"" if body is None else json.dumps(body).encode()
    return HttpResponse(status, headers or {}, raw)


def envelope(data: Any, **extra: Any) -> HttpResponse:
    """A success envelope as the services send it."""
    return reply({"data": data, "code": 200, "isOk": True, **extra})


class FakeTransport:
    """Answers each request with the next queued item; the last item repeats."""

    def __init__(self, *items: Item) -> None:
        self.queue: List[Item] = list(items) or [envelope([])]
        self.requests: List[HttpRequest] = []
        self.stream_lines: List[bytes] = []
        self.stream_status = 200

    def send(self, request: HttpRequest) -> HttpResponse:
        self.requests.append(request)
        item = self.queue.pop(0) if len(self.queue) > 1 else self.queue[0]
        if isinstance(item, Exception):
            raise item
        return item

    def stream(self, request: HttpRequest) -> StreamResponse:
        self.requests.append(request)
        return StreamResponse(self.stream_status, {}, iter(self.stream_lines))

    @property
    def last(self) -> HttpRequest:
        return self.requests[-1]


class Sleeps:
    """Records waits instead of sleeping."""

    def __init__(self) -> None:
        self.waits: List[float] = []

    def __call__(self, seconds: float) -> None:
        self.waits.append(seconds)


def query_of(request: HttpRequest) -> Dict[str, List[str]]:
    return parse_qs(urlsplit(request.url).query, keep_blank_values=True)


def path_of(request: HttpRequest) -> str:
    return urlsplit(request.url).path


def host_of(request: HttpRequest) -> str:
    parts = urlsplit(request.url)
    return f"{parts.scheme}://{parts.netloc}"


@pytest.fixture
def sleeps() -> Sleeps:
    return Sleeps()


MakeClient = Callable[..., Tuple[FinClient, FakeTransport]]


@pytest.fixture
def make_client(sleeps: Sleeps) -> MakeClient:
    def build(*items: Item, **kwargs: Any) -> Tuple[FinClient, FakeTransport]:
        transport = FakeTransport(*items)
        options: Dict[str, Any] = {
            "api_key": "copy-key",
            "graph": ServiceOptions(api_key="graph-key"),
            "news": ServiceOptions(api_key="news-key"),
            "social": ServiceOptions(api_key="social-key"),
            "transport": transport,
            "sleep": sleeps,
            "rand": lambda: 0.5,
        }
        options.update(kwargs)
        return FinClient(**options), transport

    return build
