"""The FinCopy change feed."""
from __future__ import annotations

from typing import Any, Callable, Iterator, List, Optional, TypedDict

from .._errors import ApiResponseError
from .._generated import copy_types as t
from .._service import Group


class ChangeFeedPage(TypedDict):
    """One page of the change feed."""

    Changes: List[t.DtoApiChange]
    Cursor: Optional[str]
    More: bool


def read_change_pages(
    read_page: Callable[[Optional[str]], ChangeFeedPage], cursor: Optional[str] = None
) -> Iterator[ChangeFeedPage]:
    """Follow the cursor page by page until the server reports that no more changes remain."""
    seen = {cursor} if cursor else set()
    while True:
        page = read_page(cursor)
        yield page
        if not page["More"]:
            return
        following = page["Cursor"]
        if not following or following == cursor or following in seen:
            raise ApiResponseError(
                "The FinCopy change feed returned More without a new cursor.", error_code="invalid_response")
        seen.add(following)
        cursor = following


class ChangesResource(Group):
    """Changes feed resource."""

    def get(self, cursor: Optional[str] = None, take: int = 500) -> t.DtoApiChangeFeed:
        """Read one changes page using an optional cursor."""
        if isinstance(take, bool) or not isinstance(take, int) or not 1 <= take <= 1000:
            raise ValueError("take must be between 1 and 1000.")
        result: t.DtoApiChangeFeed = self._service.data(
            "GET", "v1/changes", query=[("cursor", cursor), ("take", take)])
        return result

    def read_pages(self, cursor: Optional[str] = None, take: int = 500) -> Iterator[ChangeFeedPage]:
        """Read changes page by page, following each cursor. Save each page's ``Cursor`` to resume after a restart."""
        return read_change_pages(lambda next_cursor: self._page(next_cursor, take), cursor)

    def read_all(self, cursor: Optional[str] = None, take: int = 500) -> Iterator[t.DtoApiChange]:
        """Every change from every page, in order. Use :meth:`read_pages` when the cursor must be saved."""
        for page in self.read_pages(cursor, take):
            yield from page["Changes"]

    def _page(self, cursor: Optional[str], take: int) -> ChangeFeedPage:
        feed: Any = self.get(cursor, take)
        return {
            "Changes": feed.get("Changes") or [],
            "Cursor": feed.get("Cursor"),
            "More": bool(feed.get("More")),
        }
