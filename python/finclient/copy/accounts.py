"""FinCopy accounts: lifecycle, settings, pairs, symbols, orders, history, performance and terminal operations."""
from __future__ import annotations

from datetime import datetime
from typing import Iterator, List, Optional, Union

from .._encode import seg
from .._generated import copy_types as t
from .._paging import Page, iter_items
from .._service import Group
from ._common import (
    PageQuery,
    account_path,
    date_filters,
    iterate_pages,
    numeric_id,
    page_pairs,
    require_text,
    with_filters,
)

DateInput = Union[str, datetime, None]


class AccountsResource(Group):
    """Account lifecycle, settings, symbols, positions and results."""

    def list(self, page: Optional[PageQuery] = None, external_ref: Optional[str] = None) -> Page[t.DtoMemberAccount]:
        """List reachable accounts, optionally filtered by external reference."""
        return self._service.page(
            "GET", "v1/fxs/accounts", query=with_filters(page, externalRef=external_ref))

    def list_pages(
        self, page: Optional[PageQuery] = None, external_ref: Optional[str] = None
    ) -> Iterator[Page[t.DtoMemberAccount]]:
        """Yield every page of :meth:`list`, advancing ``PageNo`` until the last page."""
        return iterate_pages(lambda q: self.list(q, external_ref), page)

    def list_items(
        self, page: Optional[PageQuery] = None, external_ref: Optional[str] = None
    ) -> Iterator[t.DtoMemberAccount]:
        """Yield every account, page after page."""
        return iter_items(self.list_pages(page, external_ref))

    def make(self, command: t.CmdMemberAccountMake, *, idempotency_key: Optional[str] = None) -> t.DtoFleetAccount:
        """Add a hosted account."""
        return self._service.data("POST", "v1/fxs/accounts", body=command, idempotency_key=idempotency_key)

    def make_demo(self, command: t.CmdMemberDemoMake, *, idempotency_key: Optional[str] = None) -> t.DtoFleetAccount:
        """Create a demo account."""
        return self._service.data("POST", "v1/fxs/accounts/demo", body=command, idempotency_key=idempotency_key)

    def demo_availability(self, platform: Optional[str] = None) -> t.DtoDemoAvailability:
        """Read demo availability for one platform."""
        return self._service.data("GET", "v1/fxs/accounts/demo-availability", query=[("platform", platform)])

    def get(self, acno: int) -> t.DtoMemberAccount:
        """Read one account by ACNO."""
        return self._service.data("GET", account_path(acno))

    def set_external_ref(
        self, acno: int, command: t.CmdTermAccountExternalRef, *, idempotency_key: Optional[str] = None
    ) -> t.DtoTermAccount:
        """Set or clear the account's external reference."""
        return self._service.data(
            "PUT", f"{account_path(acno)}/external-ref", body=command, idempotency_key=idempotency_key)

    def get_settings(self, acno: int) -> t.DtoFxsAcct:
        """Read account settings."""
        return self._service.data("GET", f"{account_path(acno)}/settings")

    def patch_settings(
        self, acno: int, command: t.CmdApiFxsAcctPatch, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAcct:
        """Change account settings."""
        return self._service.data(
            "PATCH", f"{account_path(acno)}/settings", body=command, idempotency_key=idempotency_key)

    def pairs(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Page[t.DtoFxsPair]:
        """List pairs for one account."""
        return self._service.page("GET", f"{account_path(acno)}/pairs", query=with_filters(page, search=search))

    def get_aibot(self, acno: int) -> t.DtoAIBotFullAuto:
        """Read AIBot posture, hourly records and Advisor settings, balance and latest advice."""
        return self._service.data("GET", f"{account_path(acno)}/aibot")

    def preview_advisor(self, acno: int, kind: str = "ON_DEMAND") -> t.DtoAdvisorPreview:
        """Preview an ON_DEMAND or PLAN ask without sending or spending Advisor credits."""
        return self._service.data("GET", f"{account_path(acno)}/aibot/advisor/preview", query=[("kind", kind)])

    def send_advisor(
        self, acno: int, command: t.CmdAdvisorSend, *, idempotency_key: Optional[str] = None
    ) -> t.DtoAIBotFullAuto:
        """Send a preview or ask now with an empty PreviewId. FinCopy handles Ultra, credits and refunds."""
        return self._service.data(
            "POST", f"{account_path(acno)}/aibot/advisor/send", body=command, idempotency_key=idempotency_key)

    def save_advisor_settings(
        self, acno: int, command: t.CmdAdvisorSettings, *, idempotency_key: Optional[str] = None
    ) -> t.DtoAIBotFullAuto:
        """Save preferences; counters and included-review state remain server-owned."""
        return self._service.data(
            "PUT", f"{account_path(acno)}/aibot/advisor/settings", body=command, idempotency_key=idempotency_key)

    def advisor_history(self, acno: int) -> List[t.DtoAIBotAdvice]:
        """Latest 100 Advisor asks, newest first, including sent context and payment results."""
        return self._service.data("GET", f"{account_path(acno)}/aibot/advisor/history")

    def advisor_history_view(self, acno: int, advice_id: str) -> t.DtoAIBotAdvice:
        """Read one Advisor ask within this account; another account's id is not found."""
        return self._service.data(
            "GET", f"{account_path(acno)}/aibot/advisor/history/{seg(require_text(advice_id, 'advice_id'))}")

    def decide_aibot_advice(
        self, acno: int, command: t.CmdAIBotAdviceDecide, *, idempotency_key: Optional[str] = None
    ) -> t.DtoAIBotFullAuto:
        """Accept or decline account Advisor advice. A trade key is the owner's standing consent."""
        return self._service.data(
            "POST", f"{account_path(acno)}/aibot/advice/decide", body=command, idempotency_key=idempotency_key)

    def undo_aibot_advice(
        self, acno: int, command: t.CmdAIBotAdviceUndo, *, idempotency_key: Optional[str] = None
    ) -> t.DtoAIBotFullAuto:
        """Undo the account's last applied advice, subject to FinCopy's current safety checks."""
        return self._service.data(
            "POST", f"{account_path(acno)}/aibot/advice/undo", body=command, idempotency_key=idempotency_key)

    def aibot_decisions(self, acno: int) -> List[t.DtoAIBotFullAutoDecision]:
        """Read the account's AIBot decision record."""
        return self._service.data("GET", f"{account_path(acno)}/aibot/decisions")

    def pairs_pages(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Iterator[Page[t.DtoFxsPair]]:
        """Yield every page of :meth:`pairs`."""
        return iterate_pages(lambda q: self.pairs(acno, q, search), page)

    def pair(self, acno: int, prno: int) -> t.DtoFxsPair:
        """Read one pair by PRNO."""
        return self._service.data("GET", f"{account_path(acno)}/pairs/{numeric_id(prno, 'prno')}")

    def patch_pair(
        self, acno: int, prno: int, command: t.CmdApiFxsPairPatch, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsPair:
        """Change one pair's settings."""
        return self._service.data(
            "PATCH", f"{account_path(acno)}/pairs/{numeric_id(prno, 'prno')}", body=command,
            idempotency_key=idempotency_key)

    def symbols(self, acno: int) -> t.DtoFxsSymbolCatalog:
        """Read the broker symbols and enabled list."""
        return self._service.data("GET", f"{account_path(acno)}/symbols")

    def edit_symbols(
        self, acno: int, command: t.CmdFxsSymbolsEdit, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAccountState:
        """Add or remove enabled symbols."""
        return self._service.data(
            "PUT", f"{account_path(acno)}/symbols", body=command, idempotency_key=idempotency_key)

    def quote(self, acno: int, symbol: str) -> t.DtoFxsSymbolSpec:
        """Read a symbol's broker specification and latest quote."""
        return self._service.data("GET", f"{account_path(acno)}/quotes/{seg(require_text(symbol, 'symbol'))}")

    def pair_instrument_suggestions(self, acno: int, symbol: str) -> t.DtoPairInstrumentSuggestions:
        """Get instrument suggestions for a broker symbol."""
        return self._service.data(
            "GET", f"{account_path(acno)}/pairs/instrument-suggestions/{seg(require_text(symbol, 'symbol'))}")

    def confirm_pair_instrument(
        self, acno: int, command: t.CmdFxsPairInstrumentConfirm, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFinInstrument:
        """Confirm the FinGraph instrument mapping for a broker symbol."""
        return self._service.data(
            "PUT", f"{account_path(acno)}/pairs/instrument-confirm", body=command, idempotency_key=idempotency_key)

    def orders(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Page[t.DtoFxsOrdr]:
        """List live orders for one account."""
        return self._service.page("GET", f"{account_path(acno)}/orders", query=with_filters(page, search=search))

    def orders_pages(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Iterator[Page[t.DtoFxsOrdr]]:
        """Yield every page of :meth:`orders`."""
        return iterate_pages(lambda q: self.orders(acno, q, search), page)

    def order(self, acno: int, oid: int) -> t.DtoFxsOrdr:
        """Read one live or settled order."""
        return self._service.data("GET", f"{account_path(acno)}/orders/{numeric_id(oid, 'oid')}")

    def history(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Page[t.DtoFxsOrdh]:
        """List settled orders."""
        return self._service.page("GET", f"{account_path(acno)}/history", query=with_filters(page, search=search))

    def history_pages(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Iterator[Page[t.DtoFxsOrdh]]:
        """Yield every page of :meth:`history`."""
        return iterate_pages(lambda q: self.history(acno, q, search), page)

    def history_item(self, acno: int, oid: int) -> t.DtoFxsOrdh:
        """Read one settled order by OID."""
        return self._service.data("GET", f"{account_path(acno)}/history/{numeric_id(oid, 'oid')}")

    def dca_queue(self, acno: int) -> List[t.DtoFxsOrdr]:
        """List HYBRID DCA proposals waiting for an answer."""
        return self._service.data("GET", f"{account_path(acno)}/dca")

    def performance(
        self, acno: int, *, from_dt: DateInput = None, to_dt: DateInput = None
    ) -> List[t.DtoFxsPerf]:
        """List daily performance rows."""
        return self._service.data(
            "GET", f"{account_path(acno)}/performance", query=list(date_filters(from_dt, to_dt).items()))

    def performance_window(self, acno: int) -> t.DtoFxsPerfWindow:
        """Read the account's performance window."""
        return self._service.data("GET", f"{account_path(acno)}/performance/window")

    def performance_bars(
        self, acno: int, *, from_dt: DateInput = None, to_dt: DateInput = None
    ) -> t.DtoFxsPerfBars:
        """Read performance bars for an optional UTC date range."""
        return self._service.data(
            "GET", f"{account_path(acno)}/performance/bars", query=list(date_filters(from_dt, to_dt).items()))

    def activity(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Page[t.DtoMemberActivity]:
        """List paged account activity."""
        return self._service.page("GET", f"{account_path(acno)}/activity", query=with_filters(page, search=search))

    def activity_pages(
        self, acno: int, page: Optional[PageQuery] = None, search: Optional[str] = None
    ) -> Iterator[Page[t.DtoMemberActivity]]:
        """Yield every page of :meth:`activity`."""
        return iterate_pages(lambda q: self.activity(acno, q, search), page)

    def preview_strategy(
        self, acno: int, command: t.CmdApiFxsStgApply, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsStgCascadePreview:
        """Preview applying a strategy (yours or a FinCopy preset): what each symbol would change. Nothing changes."""
        return self._service.data(
            "POST", f"{account_path(acno)}/strategy/preview", body=command, idempotency_key=idempotency_key)

    def apply_strategy(
        self, acno: int, command: t.CmdApiFxsStgApply, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsStgCascadeApplyResult:
        """Apply a strategy to the account and its symbols; a side holding orders keeps what it must keep."""
        return self._service.data(
            "POST", f"{account_path(acno)}/strategy/apply", body=command, idempotency_key=idempotency_key)

    def operations(self, acno: int, page: Optional[PageQuery] = None) -> Page[t.DtoApiTermOperation]:
        """List recent terminal operations."""
        return self._service.page("GET", f"{account_path(acno)}/operations", query=page_pairs(page))

    def change_password(
        self, acno: int, command: t.CmdTermAccountPassword, *, idempotency_key: Optional[str] = None
    ) -> t.DtoOperationQueued:
        """Queue a broker-password change."""
        return self._service.data(
            "POST", f"{account_path(acno)}/password", body=command, idempotency_key=idempotency_key)

    def adopt_open_trades(
        self, acno: int, command: Optional[t.CmdApiFxsAdopt] = None, *, idempotency_key: Optional[str] = None
    ) -> t.DtoApiFxsAdopt:
        """Adopt open trades: every market position opened outside FinCopy becomes FinCopy's own.

        Each keeps its ticket, lot, price and open time. FinCopy enables each trade's pair within the plan's pair limit
        (oldest first); a pair that does not fit is refused whole and listed with its tickets. Send
        ``{"Preview": True}`` to see the result without changing anything. Needs the Trade permission and an EA of 2.38
        or later.
        """
        return self._service.data(
            "POST", f"{account_path(acno)}/trade/adopt", body=command or {}, idempotency_key=idempotency_key)

    def start(self, acno: int, *, idempotency_key: Optional[str] = None) -> t.DtoOperationQueued:
        """Queue a terminal start."""
        return self._operation(acno, "start", idempotency_key)

    def stop(self, acno: int, *, idempotency_key: Optional[str] = None) -> t.DtoOperationQueued:
        """Queue a terminal stop."""
        return self._operation(acno, "stop", idempotency_key)

    def restart(self, acno: int, *, idempotency_key: Optional[str] = None) -> t.DtoOperationQueued:
        """Queue a terminal restart."""
        return self._operation(acno, "restart", idempotency_key)

    def remove(
        self, acno: int, command: Optional[t.CmdMemTerminalDisconnect] = None, *, idempotency_key: Optional[str] = None
    ) -> t.DtoOperationQueued:
        """Disconnect the hosted terminal. Pass ``{"TrPin": ...}`` for a session account that needs its trading PIN."""
        return self._service.data(
            "DELETE", account_path(acno), body=command or {}, idempotency_key=idempotency_key)

    def _operation(self, acno: int, action: str, idempotency_key: Optional[str]) -> t.DtoOperationQueued:
        return self._service.data("POST", f"{account_path(acno)}/{action}", idempotency_key=idempotency_key)
