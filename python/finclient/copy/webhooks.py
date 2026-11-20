"""FinCopy webhook configuration and the signature check for deliveries."""
from __future__ import annotations

import hashlib
import hmac
import re
import time
from typing import Optional, Union

from .._generated import copy_types as t
from .._service import Group

ALLOWED_SKEW_SECONDS = 300
_HEX64 = re.compile(r"^[a-fA-F0-9]{64}$")


def verify_signature(
    secret: str,
    timestamp: Union[str, int],
    body: Union[str, bytes],
    signature: str,
    *,
    tolerance: int = ALLOWED_SKEW_SECONDS,
    now: Optional[float] = None,
) -> bool:
    """Verify the ``X-FinCopy-Signature`` HMAC-SHA256 over the timestamp and the exact raw request body.

    ``signature`` may carry a ``sha256=`` prefix. A timestamp more than ``tolerance`` seconds (default 300) from now,
    in either direction, is refused, so a captured delivery cannot be replayed later.
    """
    if not secret or not signature:
        return False
    text = str(timestamp)
    if not text.isascii() or not text.isdigit():
        return False
    moment = time.time() if now is None else now
    if abs(moment - int(text)) > tolerance:
        return False
    supplied = signature[7:] if signature.startswith("sha256=") else signature
    if not _HEX64.match(supplied):
        return False
    payload = body.encode("utf-8") if isinstance(body, str) else bytes(body)
    expected = hmac.new(secret.encode("utf-8"), f"{text}.".encode() + payload, hashlib.sha256).digest()
    return hmac.compare_digest(bytes.fromhex(supplied), expected)


class FinWebhook:
    """The webhook signature check, as a class for parity with the other clients."""

    verify = staticmethod(verify_signature)


class WebhooksResource(Group):
    """Webhook configuration and test delivery."""

    def get(self) -> t.DtoApiWebhook:
        """Read this key's webhook configuration."""
        return self._service.data("GET", "v1/webhook")

    def set(self, command: t.CmdApiWebhookSet, *, idempotency_key: Optional[str] = None) -> t.DtoApiWebhook:
        """Set the webhook destination. The reply carries its signing secret once."""
        return self._service.data("PUT", "v1/webhook", body=command, idempotency_key=idempotency_key)

    def delete(self, *, idempotency_key: Optional[str] = None) -> bool:
        """Remove this key's webhook."""
        return bool(self._service.data("DELETE", "v1/webhook", idempotency_key=idempotency_key))

    def ping(self, *, idempotency_key: Optional[str] = None) -> t.DtoApiWebhookPing:
        """Send a signed test delivery."""
        return self._service.data("POST", "v1/webhook/ping", idempotency_key=idempotency_key)
