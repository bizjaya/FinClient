"""Pages of rows: the :class:`Page` a list returns and the iterators that follow it."""
from __future__ import annotations

from typing import Any, Callable, Dict, Generic, Iterable, Iterator, List, Mapping, Optional, Tuple, TypedDict, TypeVar

__all__ = ["Page", "PageInfo", "iter_items", "iter_pages", "iter_qry_pages"]

T = TypeVar("T")


class PageInfo(TypedDict, total=False):
    """The paging block of a list reply, as the API sends it."""

    pageNo: int
    pageSize: int
    pageCount: int
    resCount: int


class Page(Generic[T]):
    """One page of a list: ``items``, the API's ``paging`` block and ``not_found``, the symbols a ``syms`` lookup named
    that no row carries. Iterating a page yields its items."""

    __slots__ = ("items", "paging", "code", "message", "errors", "chksum", "not_found")

    def __init__(
        self,
        items: List[T],
        paging: Optional[PageInfo] = None,
        code: int = 200,
        message: Optional[str] = None,
        errors: Tuple[str, ...] = (),
        chksum: Optional[str] = None,
        not_found: Tuple[str, ...] = (),
    ) -> None:
        self.items = items
        self.paging = paging
        self.code = code
        self.message = message
        self.errors = errors
        self.chksum = chksum
        self.not_found = not_found

    def __iter__(self) -> Iterator[T]:
        return iter(self.items)

    def __len__(self) -> int:
        return len(self.items)

    def __getitem__(self, index: int) -> T:
        return self.items[index]

    def __repr__(self) -> str:
        return f"Page(items={len(self.items)}, paging={self.paging!r})"


def iter_qry_pages(
    fetch: Callable[..., "Page[T]"],
    *path_args: Any,
    qry: Mapping[str, Any],
    number_key: str = "pageNo",
    size_key: str = "pageSize",
) -> Iterator["Page[T]"]:
    """Call ``fetch(*path_args, qry)`` for page 1, 2, ... until the last page or an empty page."""
    query: Dict[str, Any] = dict(qry)
    number = int(query.get(number_key) or 1)
    while True:
        query[number_key] = number
        page = fetch(*path_args, dict(query))
        if not page.items:
            return
        yield page
        info: Mapping[str, Any] = page.paging or {}
        count = info.get("pageCount")
        if count is not None:
            if number >= count:
                return
        else:
            size = query.get(size_key) or info.get("pageSize")
            if not size or len(page.items) < size:
                return
        number += 1


def iter_pages(fetch: Callable[[int], "Page[T]"], *, first: Optional[int] = None) -> Iterator["Page[T]"]:
    """Call ``fetch(number)`` for page ``first`` (1 when unset), then the next, until the last page or an empty page."""
    number = first or 1
    while True:
        page = fetch(number)
        if not page.items:
            return
        yield page
        info: Mapping[str, Any] = page.paging or {}
        shown = info.get("pageNo") or number
        count = info.get("pageCount")
        if count is not None and shown >= count:
            return
        number = shown + 1


def iter_items(pages: Iterable[Any]) -> Iterator[Any]:
    """Every row of every page, in order. Accepts pages from either kind of iterator."""
    for page in pages:
        yield from (page.items if isinstance(page, Page) else page)
