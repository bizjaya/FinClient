"""The exceptions FinClient raises. Every one derives from :class:`FinApiError`."""
from __future__ import annotations

from typing import Any, Optional, Sequence, Union

__all__ = [
    "FinApiError",
    "ApiConnectionError",
    "ApiResponseError",
    "AuthenticationError",
    "InsufficientCreditsError",
    "MissingApiKeyError",
    "NotFoundError",
    "PermissionDeniedError",
    "RateLimitError",
    "ServerError",
    "error_for_status",
]


class FinApiError(Exception):
    """An unsuccessful API call.

    Attributes:
        status_code: The HTTP status, or 0 when no response arrived (timeout, network failure or a missing key).
        code: The API's own error code (``invalid_api_key``, ``insufficient_credits``, ``package_not_allowed`` ...)
            or, for FinCopy, its numeric response code. ``None`` when the body gave none.
        message: A human-readable explanation.
        errors: Further messages the API listed.
        error_code: A short reason set by the SDK: ``timeout``, ``network``, ``rate_limited``, ``invalid_response``,
            ``empty_response`` or ``missing_api_key``; ``None`` for errors the API itself reported.
        retry_after: Seconds the API asked the caller to wait (``Retry-After``), when it said.
        lane: On limit refusals, the setting that governs the limit that was hit.
        limit: On limit refusals, the value that was hit.
        catag: On per-category limits, the category tag.
        tier: On per-category limits, the caller's package tier.
    """

    def __init__(
        self,
        message: str,
        *,
        status_code: int = 0,
        code: Union[str, int, None] = None,
        errors: Sequence[str] = (),
        error_code: Optional[str] = None,
        retry_after: Optional[float] = None,
        lane: Optional[str] = None,
        limit: Any = None,
        catag: Optional[str] = None,
        tier: Optional[str] = None,
    ) -> None:
        super().__init__(message or "The request failed.")
        self.message = message or "The request failed."
        self.status_code = status_code
        self.code = code
        self.errors = tuple(errors)
        self.error_code = error_code
        self.retry_after = retry_after
        self.lane = lane
        self.limit = limit
        self.catag = catag
        self.tier = tier

    def __repr__(self) -> str:
        return f"{type(self).__name__}(status_code={self.status_code!r}, code={self.code!r}, message={self.message!r})"


class ApiConnectionError(FinApiError):
    """No answer arrived: the request timed out or the network failed. ``error_code`` is ``timeout`` or ``network``."""


class ApiResponseError(FinApiError):
    """The API answered, but not with a usable body (``invalid_response`` or ``empty_response``)."""


class AuthenticationError(FinApiError):
    """HTTP 401: the key is missing, unknown or disabled."""


class MissingApiKeyError(AuthenticationError):
    """A part of the client was used without an API key. Raised before any request is sent."""


class InsufficientCreditsError(FinApiError):
    """HTTP 402: not enough credits, or a credit budget or call limit was reached."""


class PermissionDeniedError(FinApiError):
    """HTTP 403: the key's package may not call this operation, or the operation is in preview."""


class NotFoundError(FinApiError):
    """HTTP 404."""


class RateLimitError(FinApiError):
    """HTTP 429: a rate or call limit was reached. ``retry_after`` says how long to wait, when the API said."""


class ServerError(FinApiError):
    """HTTP 5xx."""


def error_for_status(status: int, message: str, **details: Any) -> FinApiError:
    """The exception class that matches an HTTP status."""
    if status == 401:
        cls: type = AuthenticationError
    elif status == 402:
        cls = InsufficientCreditsError
    elif status == 403:
        cls = PermissionDeniedError
    elif status == 404:
        cls = NotFoundError
    elif status == 429:
        cls = RateLimitError
        details.setdefault("error_code", "rate_limited")
    elif status >= 500:
        cls = ServerError
    else:
        cls = FinApiError
    error: FinApiError = cls(message, status_code=status, **details)
    return error


