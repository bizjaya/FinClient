"""One service's request engine: authentication, retries with backoff, error mapping and envelope handling."""
from __future__ import annotations

import json
import random as _random
import time
import uuid
from email.utils import parsedate_to_datetime
from typing import Any, Callable, Dict, Iterable, List, Mapping, Optional, Tuple

from ._encode import encode_body, encode_query
from ._errors import (
    ApiConnectionError,
    ApiResponseError,
    FinApiError,
    MissingApiKeyError,
    error_for_status,
)
from ._paging import Page, PageInfo
from ._transport import HttpRequest, StreamResponse, Transport, TransportError
from ._version import __version__

__all__ = ["Group", "Service"]

_RETRY_STATUSES = frozenset({429, 502, 503, 504})
_SAFE_METHODS = frozenset({"GET", "HEAD", "OPTIONS", "PUT", "DELETE"})
_WRITE_METHODS = frozenset({"POST", "PUT", "PATCH", "DELETE"})
_MAX_SERVER_WAIT = 60.0
_MAX_BACKOFF = 30.0


class Service:
    """The connection to one service: its base URL, key and header, and the retry policy.

    ``strict`` is FinCopy's envelope contract: every reply carries ``isOk`` and a non-null ``data``, and every write
    carries an ``Idempotency-Key``. The other services answer plainly and take neither.
    """

    def __init__(
        self,
        *,
        name: str,
        base_url: str,
        api_key: Optional[str],
        key_header: str,
        transport: Transport,
        timeout: float,
        max_retries: int,
        sleep: Callable[[float], None] = time.sleep,
        rand: Callable[[], float] = _random.random,
        strict: bool = False,
        key_hint: str = "",
    ) -> None:
        self.name = name
        self.base_url = base_url.rstrip("/")
        self.api_key = (api_key or "").strip()
        self.key_header = key_header
        self.transport = transport
        self.timeout = timeout
        self.max_retries = max_retries
        self.strict = strict
        self.key_hint = key_hint
        self._sleep = sleep
        self._rand = rand

    # -- public calls -----------------------------------------------------------------------------------------------

    def data(self, method: str, path: str, **kwargs: Any) -> Any:
        """Send a request and return the reply's ``data`` (the whole reply when it has no ``data``)."""
        payload = self.request(method, path, **kwargs)
        if isinstance(payload, dict) and "data" in payload:
            result = payload["data"]
            if result is None and self.strict:
                raise ApiResponseError(
                    f"The {self.name} API returned an empty response.", status_code=200, error_code="empty_response")
            return result
        return payload

    def page(self, method: str, path: str, **kwargs: Any) -> Page[Any]:
        """Send a request whose reply is a page: ``data`` rows plus a ``paging`` block."""
        payload = self.request(method, path, **kwargs)
        rows = payload.get("data") if isinstance(payload, dict) else None
        if not isinstance(rows, list):
            raise ApiResponseError(
                f"The {self.name} API returned an invalid page.", status_code=200, error_code="invalid_response")
        paging: Optional[PageInfo] = payload.get("paging")
        return Page(
            rows,
            paging,
            code=payload.get("code", 200),
            message=payload.get("message"),
            errors=tuple(str(e) for e in payload.get("errors") or ()),
            chksum=payload.get("chksum"),
        )

    def request(
        self,
        method: str,
        path: str,
        *,
        auth: str = "key",
        query: Optional[Iterable[Tuple[str, Any]]] = None,
        qry: Optional[Mapping[str, Any]] = None,
        body: Any = None,
        idempotency_key: Optional[str] = None,
    ) -> Any:
        """Send one request and return the decoded JSON reply, retrying where it is safe to."""
        verb = method.upper()
        self._require_key(auth)
        url = self._url(path, query, qry)
        headers = self._headers("application/json")
        payload: Optional[bytes] = None
        if body is not None:
            payload = encode_body(body)
            headers["Content-Type"] = "application/json"
        repeatable = verb in _SAFE_METHODS
        if self.strict and verb in _WRITE_METHODS:
            headers["Idempotency-Key"] = _idempotency_key(idempotency_key)
            repeatable = True
        request = HttpRequest(verb, url, headers, payload, self.timeout)

        retry = 0
        while True:
            try:
                response = self.transport.send(request)
            except TransportError as error:
                if repeatable and retry < self.max_retries:
                    self._sleep(self._backoff(retry))
                    retry += 1
                    continue
                if error.timeout:
                    raise ApiConnectionError(
                        f"The {self.name} API did not answer in time.", error_code="timeout") from error
                raise ApiConnectionError(
                    f"The {self.name} API could not be reached.", error_code="network") from error

            wait = _retry_after(response.header("Retry-After"))
            # A 429 was refused before it was processed, so it is safe to repeat for any method; a 5xx only for
            # requests that can be repeated without a second effect.
            if response.status in _RETRY_STATUSES and (response.status == 429 or repeatable):
                if retry < self.max_retries and (wait is None or wait <= _MAX_SERVER_WAIT):
                    delay = wait if wait is not None else (
                        self._rate_backoff(retry) if response.status == 429 else self._backoff(retry))
                    self._sleep(delay)
                    retry += 1
                    continue
            return self._decode(response.status, response.body, wait)

    def stream(self, path: str, *, auth: str = "key") -> StreamResponse:
        """Open a server-sent event stream. It does not reconnect."""
        self._require_key(auth)
        request = HttpRequest(
            "GET", self._url(path, None, None), self._headers("text/event-stream"), None, self.timeout)
        try:
            response = self.transport.stream(request)
        except TransportError as error:
            raise ApiConnectionError(
                f"The {self.name} event stream could not be opened.", error_code="network") from error
        if response.status >= 400:
            self._decode(response.status, response.body, _retry_after(response.header("Retry-After")))
        return response

    # -- internals --------------------------------------------------------------------------------------------------

    def _require_key(self, auth: str) -> None:
        if auth != "none" and not self.api_key:
            raise MissingApiKeyError(
                f"{self.name} needs an API key. {self.key_hint}".strip(), error_code="missing_api_key")

    def _headers(self, accept: str) -> Dict[str, str]:
        headers = {"Accept": accept, "User-Agent": f"finclient-python/{__version__}"}
        if self.api_key:
            headers[self.key_header] = self.api_key
        return headers

    def _url(self, path: str, query: Optional[Iterable[Tuple[str, Any]]], qry: Optional[Mapping[str, Any]]) -> str:
        text = encode_query(query, qry)
        return f"{self.base_url}/{path.lstrip('/')}" + (f"?{text}" if text else "")

    def _backoff(self, retry: int) -> float:
        return min(_MAX_BACKOFF, 0.25 * 2.0**retry * (0.75 + self._rand() * 0.5))

    @staticmethod
    def _rate_backoff(retry: int) -> float:
        """A 429 without Retry-After: whole seconds (1, 2, 4 ... at most 30)."""
        return min(_MAX_BACKOFF, max(1.0, 2.0**retry))

    def _decode(self, status: int, raw: bytes, retry_after: Optional[float]) -> Any:
        payload: Any = None
        if raw.strip():
            try:
                payload = json.loads(raw)
            except ValueError:
                payload = None
        ok_flag = payload.get("isOk") if isinstance(payload, dict) else None
        if 200 <= status < 300 and ok_flag is not False:
            if self.strict and ok_flag is not True:
                raise ApiResponseError(
                    f"The {self.name} API returned an unusable response.", status_code=status,
                    error_code="empty_response" if payload is None and not raw.strip() else "invalid_response")
            if payload is None:
                raise ApiResponseError(
                    f"The {self.name} API returned an unusable response.", status_code=status,
                    error_code="empty_response" if not raw.strip() else "invalid_response")
            return payload
        raise _error_from(self.name, status, payload, retry_after)


def _error_from(name: str, status: int, payload: Any, retry_after: Optional[float]) -> FinApiError:
    """Read the error from any of the body shapes: {error, message}, {error: {code, message}} or an envelope."""
    message: Any = None
    code: Any = None
    errors: List[str] = []
    extras: Dict[str, Any] = {}
    if isinstance(payload, dict):
        found = payload.get("error")
        if isinstance(found, dict):
            code = found.get("code") or found.get("Code")
            message = found.get("message") or found.get("Message")
            errors.extend(_error_text(e) for e in found.get("Errors") or ())
        elif isinstance(found, str):
            code = found
        else:
            code = payload.get("code")
        if not message:
            message = payload.get("message")
        errors.extend(_error_text(e) for e in payload.get("errors") or ())
        for key in ("lane", "limit", "catag", "tier"):
            if key in payload:
                extras[key] = payload[key]
    text = message if isinstance(message, str) and message else f"The {name} API request failed with HTTP {status}."
    if 200 <= status < 300:
        return ApiResponseError(text, status_code=status, code=code, errors=errors, **extras)
    return error_for_status(status, text, code=code, errors=errors, retry_after=retry_after, **extras)


def _error_text(value: Any) -> str:
    return value if isinstance(value, str) else json.dumps(value)


def _retry_after(header: Optional[str]) -> Optional[float]:
    """Retry-After in seconds or as an HTTP date; empty, malformed or past values read as "not given"."""
    text = (header or "").strip()
    if not text:
        return None
    if text.isdigit():
        return float(text)
    try:
        moment = parsedate_to_datetime(text)
    except (TypeError, ValueError):
        return None
    wait = moment.timestamp() - time.time()
    return wait if wait > 0 else None


def _idempotency_key(supplied: Optional[str]) -> str:
    key = (supplied or "").strip()
    if not key:
        return str(uuid.uuid4())
    if len(key) > 120 or any(ord(c) < 32 or ord(c) == 127 for c in key):
        raise ValueError("idempotency_key must be 1-120 characters without control characters.")
    return key


class Group:
    """Base of every resource class: it holds the service its calls go through."""

    def __init__(self, service: Service) -> None:
        self._service = service
