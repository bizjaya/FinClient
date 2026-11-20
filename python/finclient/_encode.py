"""Encoding of paths, query strings and request bodies."""
from __future__ import annotations

import json
import re
from datetime import date, datetime
from typing import Any, Iterable, List, Mapping, Optional, Sequence, Tuple, Union
from urllib.parse import quote

__all__ = ["drop_none", "encode_query", "seg", "snake"]


def snake(name: str) -> str:
    """``pageNo`` -> ``page_no``, ``MaxPerKey`` -> ``max_per_key``."""
    text = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", name)
    text = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", text)
    return text.lower()


def seg(value: Any) -> str:
    """One URL path segment, percent-encoded."""
    text = str(value)
    if not text.strip():
        raise ValueError("A path parameter must not be empty.")
    return quote(text, safe="")


def _scalar(value: Any) -> Any:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    return value


def drop_none(value: Any) -> Any:
    """The value with every ``None`` removed, nested ones too, as FinCopy reads a missing field and a null one alike."""
    if isinstance(value, Mapping):
        return {key: drop_none(item) for key, item in value.items() if item is not None}
    if isinstance(value, (list, tuple)):
        return [drop_none(item) for item in value]
    return _scalar(value)


def _query_value(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(_scalar(value))


def encode_query(
    query: Optional[Iterable[Tuple[str, Any]]] = None,
    extra: Optional[Iterable[Tuple[str, Any]]] = None,
) -> str:
    """The query string (without ``?``); values that are None are left out and a list repeats its name."""
    pairs: List[Tuple[str, str]] = []
    for name, value in list(query or []) + list(extra or []):
        if value is None:
            continue
        if isinstance(value, (list, tuple)):
            pairs.extend((name, _query_value(item)) for item in value if item is not None)
        else:
            pairs.append((name, _query_value(value)))
    return "&".join(f"{quote(name, safe='')}={quote(value, safe='')}" for name, value in pairs)


def _json_default(value: Any) -> Any:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    raise TypeError(f"{type(value).__name__} is not JSON serializable.")


def encode_body(body: Union[Mapping[str, Any], Sequence[Any]]) -> bytes:
    return json.dumps(drop_none(body), separators=(",", ":"), default=_json_default).encode("utf-8")
