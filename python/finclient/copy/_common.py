"""Shared pieces of the FinCopy resources: paging and filter inputs and path helpers."""
from __future__ import annotations

from datetime import date, datetime
from typing import Any, Dict, Iterator, List, Optional, Sequence, Tuple, TypedDict, TypeVar, Union

from .._paging import Page, iter_qry_pages

T = TypeVar("T")

Pairs = List[Tuple[str, Any]]


class PageQuery(TypedDict, total=False):
    """Paging and search inputs for FinCopy lists. Keys keep the API's wire names."""

    PageNo: int
    PageSize: int
    Search: str
    SortBy: Sequence[Sequence[str]]
    SrchBy: Sequence[Sequence[str]]
    Include: Sequence[str]


def page_pairs(page: Optional[PageQuery]) -> Pairs:
    pairs: Pairs = []
    if not page:
        return pairs
    if page.get("PageNo") is not None:
        pairs.append(("PageNo", page["PageNo"]))
    if page.get("PageSize") is not None:
        pairs.append(("PageSize", page["PageSize"]))
    if page.get("Search") is not None:
        pairs.append(("search", page["Search"]))
    for key, clauses in (("SortBy", page.get("SortBy")), ("SrchBy", page.get("SrchBy"))):
        for index, clause in enumerate(clauses or ()):
            for part, value in enumerate(clause):
                pairs.append((f"{key}[{index}][{part}]", value))
    for value in page.get("Include") or ():
        pairs.append(("Include", value))
    return pairs


def with_filters(page: Optional[PageQuery], **filters: Any) -> Pairs:
    """Paging pairs, then each filter that is set (a filter replaces a paging pair of the same name)."""
    pairs = page_pairs(page)
    for name, value in filters.items():
        if value is None:
            continue
        pairs = [pair for pair in pairs if pair[0] != name]
        pairs.append((name, value))
    return pairs


def account_path(acno: int) -> str:
    if isinstance(acno, bool) or not isinstance(acno, int) or acno <= 0:
        raise ValueError("acno must be a positive integer.")
    return f"v1/fxs/accounts/{acno}"


def numeric_id(value: int, name: str, allow_zero: bool = True) -> str:
    if isinstance(value, bool) or not isinstance(value, int) or value < (0 if allow_zero else 1):
        raise ValueError(f"{name} is out of range.")
    return str(value)


def date_value(value: Union[str, date, datetime, None]) -> Optional[str]:
    if value is None:
        return None
    return value if isinstance(value, str) else value.isoformat()


def date_filters(from_dt: Union[str, datetime, None], to_dt: Union[str, datetime, None]) -> Dict[str, Optional[str]]:
    return {"fromDt": date_value(from_dt), "toDt": date_value(to_dt)}


def require_text(value: str, name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} is required.")
    return value


def iterate_pages(
    fetch: Any, page: Optional[PageQuery]
) -> Iterator[Page[Any]]:
    """Pages of a FinCopy list, advancing ``PageNo``. ``fetch`` takes a :class:`PageQuery`."""
    return iter_qry_pages(fetch, qry=dict(page or {}), number_key="PageNo", size_key="PageSize")
