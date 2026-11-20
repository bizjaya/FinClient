"""The HTTP layer. The default transport uses only the standard library; tests and callers may supply their own."""
from __future__ import annotations

import socket
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from typing import Dict, Iterator, Mapping, Optional, Protocol

__all__ = ["HttpRequest", "HttpResponse", "StreamResponse", "Transport", "TransportError", "UrllibTransport"]


@dataclass(frozen=True)
class HttpRequest:
    """One request, ready to send."""

    method: str
    url: str
    headers: Mapping[str, str]
    body: Optional[bytes] = None
    timeout: float = 30.0


@dataclass(frozen=True)
class HttpResponse:
    """A complete response."""

    status: int
    headers: Mapping[str, str] = field(default_factory=dict)
    body: bytes = b""

    def header(self, name: str) -> Optional[str]:
        wanted = name.lower()
        for key, value in self.headers.items():
            if key.lower() == wanted:
                return value
        return None


@dataclass(frozen=True)
class StreamResponse:
    """A response whose body is read line by line (server-sent events)."""

    status: int
    headers: Mapping[str, str]
    lines: Iterator[bytes]
    body: bytes = b""

    def header(self, name: str) -> Optional[str]:
        return HttpResponse(self.status, self.headers).header(name)


class TransportError(Exception):
    """The request got no response. ``timeout`` tells a timeout from other network failures."""

    def __init__(self, message: str, *, timeout: bool = False) -> None:
        super().__init__(message)
        self.timeout = timeout


class Transport(Protocol):
    """Anything that can send a request. Raise :class:`TransportError` when no response arrives."""

    def send(self, request: HttpRequest) -> HttpResponse: ...

    def stream(self, request: HttpRequest) -> StreamResponse: ...


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """A redirect is returned as it is, so an API key is never forwarded to another host."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):  # type: ignore[no-untyped-def]
        return None


class UrllibTransport:
    """The default transport, built on :mod:`urllib.request`."""

    def __init__(self) -> None:
        self._opener = urllib.request.build_opener(_NoRedirect)

    def _open(self, request: HttpRequest):  # type: ignore[no-untyped-def]
        prepared = urllib.request.Request(  # noqa: S310 - the scheme is checked when the client is built
            request.url, data=request.body, headers=dict(request.headers), method=request.method)
        try:
            return self._opener.open(prepared, timeout=request.timeout)
        except urllib.error.HTTPError as error:
            return error
        except (socket.timeout, TimeoutError) as error:
            raise TransportError("The request timed out.", timeout=True) from error
        except urllib.error.URLError as error:
            timed_out = isinstance(error.reason, (socket.timeout, TimeoutError))
            raise TransportError(str(error.reason), timeout=timed_out) from error
        except OSError as error:
            raise TransportError(str(error)) from error

    def send(self, request: HttpRequest) -> HttpResponse:
        response = self._open(request)
        try:
            try:
                body = response.read()
            except (socket.timeout, TimeoutError) as error:
                raise TransportError("The request timed out.", timeout=True) from error
            except OSError as error:
                raise TransportError(str(error)) from error
            headers: Dict[str, str] = {key: value for key, value in response.headers.items()}
            return HttpResponse(response.status, headers, body)
        finally:
            response.close()

    def stream(self, request: HttpRequest) -> StreamResponse:
        response = self._open(request)
        headers: Dict[str, str] = {key: value for key, value in response.headers.items()}
        if response.status >= 400:
            try:
                return StreamResponse(response.status, headers, iter(()), response.read())
            finally:
                response.close()

        def lines() -> Iterator[bytes]:
            try:
                while True:
                    try:
                        line = response.readline()
                    except (socket.timeout, TimeoutError) as error:
                        raise TransportError("The event stream timed out.", timeout=True) from error
                    except OSError as error:
                        raise TransportError(str(error)) from error
                    if not line:
                        return
                    yield line.rstrip(b"\r\n")
            finally:
                response.close()

        return StreamResponse(response.status, headers, lines())
