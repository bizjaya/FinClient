"""FinCopy trading actions. A successful write reports that work is queued for the terminal, not that it filled."""
from __future__ import annotations

from typing import Optional

from .._generated import copy_types as t
from .._service import Group
from ._common import account_path, numeric_id


class OrdersResource(Group):
    """Account-level trading actions. Follow the stream, the change feed or a webhook to see the result."""

    def pre_info(
        self, acno: int, command: t.CmdApiFxsPreInfo, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsPreInfo:
        """Preflight a trade request."""
        return self._service.data(
            "POST", f"{account_path(acno)}/trade/preinfo", body=command, idempotency_key=idempotency_key)

    def make(
        self, acno: int, command: t.CmdApiFxsOrdrMake, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAccountState:
        """Send one trade action. Pass an ``idempotency_key`` when you may repeat the same call."""
        return self._service.data(
            "POST", f"{account_path(acno)}/trade", body=command, idempotency_key=idempotency_key)

    def hedge(
        self, acno: int, command: t.CmdApiFxsHedgeMake, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAccountState:
        """Send paired BUY and SELL actions."""
        return self._service.data(
            "POST", f"{account_path(acno)}/trade/hedge", body=command, idempotency_key=idempotency_key)

    def modify(
        self, acno: int, command: t.CmdFxsModify, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAccountState:
        """Modify a trade or pending order."""
        return self._service.data(
            "POST", f"{account_path(acno)}/trade/modify", body=command, idempotency_key=idempotency_key)

    def cancel_waiting(
        self, acno: int, command: t.CmdFxsCancelWaiting, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsAccountState:
        """Cancel an order that has not reached the terminal."""
        return self._service.data(
            "POST", f"{account_path(acno)}/trade/cancel", body=command, idempotency_key=idempotency_key)

    def answer_dca(
        self, acno: int, oid: int, command: t.CmdFxsDcaAnswer, *, idempotency_key: Optional[str] = None
    ) -> t.DtoFxsOrdr:
        """Accept or reject one DCA proposal."""
        return self._service.data(
            "POST", f"{account_path(acno)}/dca/{numeric_id(oid, 'oid')}", body=command,
            idempotency_key=idempotency_key)
