"""FinCopy lists across every reachable account, strategies, signals, alerts, brokers and the live event stream."""
from __future__ import annotations

from datetime import datetime
from typing import Any, Iterator, List, Optional, Union

from .._errors import ApiConnectionError
from .._generated import copy_types as t
from .._paging import Page, iter_items
from .._service import Group
from .._transport import TransportError
from ._common import PageQuery, date_filters, iterate_pages, numeric_id, page_pairs, with_filters
from .sse import SseParser, StreamEvent

DateInput = Union[str, datetime, None]


def _strategy_path(isno: int) -> str:
    return f"v1/fxs/strategies/{numeric_id(isno, 'isno')}"


class FxResource(Group):
    """Cross-account FXS lists and lane-wide member resources."""

    def _list(
        self,
        path: str,
        page: Optional[PageQuery],
        acno: Optional[int],
        prno: Optional[int],
        demo: Optional[bool],
        platform: Optional[str],
        search: Optional[str],
        external_ref: Optional[str],
    ) -> Any:
        query = with_filters(
            page, acno=acno, prno=prno, demo=demo, platform=platform, search=search, externalRef=external_ref)
        return self._service.page("GET", path, query=query)

    def pairs(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Page[t.DtoFxsPair]:
        """List pairs across reachable accounts."""
        return self._list("v1/fxs/pairs", page, acno, prno, demo, platform, search, external_ref)

    def orders(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Page[t.DtoFxsOrdr]:
        """List open orders across reachable accounts."""
        return self._list("v1/fxs/orders", page, acno, prno, demo, platform, search, external_ref)

    def orders_pages(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Iterator[Page[t.DtoFxsOrdr]]:
        """Yield every page of :meth:`orders`."""
        return iterate_pages(
            lambda q: self.orders(
                q, acno=acno, prno=prno, demo=demo, platform=platform, search=search, external_ref=external_ref),
            page)

    def orders_items(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Iterator[t.DtoFxsOrdr]:
        """Yield every open order, page after page."""
        return iter_items(self.orders_pages(
            page, acno=acno, prno=prno, demo=demo, platform=platform, search=search, external_ref=external_ref))

    def history(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Page[t.DtoFxsOrdh]:
        """List settled orders across reachable accounts."""
        return self._list("v1/fxs/history", page, acno, prno, demo, platform, search, external_ref)

    def history_pages(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Iterator[Page[t.DtoFxsOrdh]]:
        """Yield every page of :meth:`history`."""
        return iterate_pages(
            lambda q: self.history(
                q, acno=acno, prno=prno, demo=demo, platform=platform, search=search, external_ref=external_ref),
            page)

    def activity(
        self,
        page: Optional[PageQuery] = None,
        *,
        acno: Optional[int] = None,
        prno: Optional[int] = None,
        demo: Optional[bool] = None,
        platform: Optional[str] = None,
        search: Optional[str] = None,
        external_ref: Optional[str] = None,
    ) -> Page[t.DtoMemberActivity]:
        """List activity across reachable accounts."""
        return self._list("v1/fxs/activity", page, acno, prno, demo, platform, search, external_ref)

    def dca(self, acno: Optional[int] = None) -> List[t.DtoFxsOrdr]:
        """HYBRID DCA proposals waiting for an answer across reachable accounts, newest first."""
        return self._service.data("GET", "v1/fxs/dca", query=[("acno", acno)])

    def operations(self, page: Optional[PageQuery] = None, acno: Optional[int] = None) -> Page[t.DtoApiTermOperation]:
        """The newest terminal operations across reachable accounts."""
        return self._service.page("GET", "v1/fxs/operations", query=page_pairs(page) + [("acno", acno)])

    def performance_window(
        self, *, acno: Optional[int] = None, demo: Optional[bool] = None
    ) -> t.DtoFxsPerfWindow:
        """The last 30 days by currency book across reachable accounts."""
        return self._service.data("GET", "v1/fxs/performance/window", query=[("acno", acno), ("demo", demo)])

    def performance_bars(
        self,
        *,
        acno: Optional[int] = None,
        demo: Optional[bool] = None,
        from_dt: DateInput = None,
        to_dt: DateInput = None,
    ) -> t.DtoFxsPerfBars:
        """Daily performance bars across reachable accounts for an optional UTC date range."""
        query = [("acno", acno), ("demo", demo)] + list(date_filters(from_dt, to_dt).items())
        return self._service.data("GET", "v1/fxs/performance/bars", query=query)

    def strategies(self) -> List[t.DtoFxsStg]:
        """List the strategies you can apply: your own and FinCopy's presets."""
        return self._service.data("GET", "v1/fxs/strategies")

    def strategy(self, isno: int) -> t.DtoFxsStg:
        """Read one strategy."""
        return self._service.data("GET", _strategy_path(isno))

    def make_strategy(self, command: t.CmdFxsStgMake, *, idempotency_key: Optional[str] = None) -> t.DtoFxsStg:
        """Create one of your own strategies."""
        return self._service.data("POST", "v1/fxs/strategies", body=command, idempotency_key=idempotency_key)

    def patch_strategy(
        self, isno: int, command: t.CmdFxsStgPatch, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsStg:
        """Change one of your own strategies; fields left out keep their values."""
        return self._service.data("PATCH", _strategy_path(isno), body=command, idempotency_key=idempotency_key)

    def delete_strategy(self, isno: int, *, idempotency_key: Optional[str] = None) -> bool:
        """Delete one of your own strategies."""
        return bool(self._service.data("DELETE", _strategy_path(isno), idempotency_key=idempotency_key))

    def signals(self) -> t.DtoShelfSignals:
        """Read the signals shown for enabled symbols."""
        return self._service.data("GET", "v1/fxs/signals")

    def alerts(self, page: Optional[PageQuery] = None) -> Page[t.DtoMemberActivity]:
        """List warning and error activity."""
        return self._service.page("GET", "v1/fxs/alerts", query=page_pairs(page))

    def stream(self) -> Iterator[StreamEvent]:
        """Open the live event stream for reachable accounts and yield its events.

        It does not reconnect: it ends when the server closes it (about every 30 minutes) or the connection breaks, so
        open it again in a loop, waiting a few seconds between attempts.
        """
        response = self._service.stream("v1/fxs/stream")
        parser = SseParser()
        try:
            for raw in response.lines:
                event = parser.add(raw.decode("utf-8", "replace"))
                if event is not None:
                    yield event
            final = parser.complete()
            if final is not None:
                yield final
        except TransportError as error:
            raise ApiConnectionError("The FinCopy event stream was interrupted.", error_code="network") from error
        finally:
            close = getattr(response.lines, "close", None)
            if close is not None:
                close()

    def brokers(
        self, query: Optional[str] = None, platform: Optional[str] = None, demos_only: Optional[bool] = None
    ) -> List[t.DtoBrokerServerSearch]:
        """Search broker servers available when adding an account."""
        return self._service.data(
            "GET", "v1/brokers", query=[("query", query), ("Platform", platform), ("DemosOnly", demos_only)])

    def join_waitlist(
        self, command: t.CmdWaitlistJoin, *, idempotency_key: Optional[str] = None
    ) -> t.DtoWaitlistState:
        """Join the caller's account waitlist."""
        return self._service.data("POST", "v1/fxs/waitlist/join", body=command, idempotency_key=idempotency_key)
