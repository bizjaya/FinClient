from __future__ import annotations

import json
from urllib.parse import urlsplit

from tests.conftest import MakeClient, envelope, path_of, query_of


def test_advisor_routes_preserve_owner_account_commands_and_idempotency(make_client: MakeClient) -> None:
    client, transport = make_client(
        envelope({"PreviewId": "preview"}),
        envelope({"Id": "ask", "Status": "QUEUED"}),
        envelope({"ACNO": 42, "Balance": 30}),
        envelope({"ACNO": 42, "Balance": 30}),
        envelope([{"Id": "ask", "Refunded": True}],
                 paging={"pageNo": 2, "pageSize": 10, "pageCount": 2, "resCount": 11}),
        envelope({"Id": "ask", "Status": "READY"}),
    )
    a = client.copy.accounts
    assert a.preview_advisor(42, "PLAN", ["EURUSD", "XAUUSD"])["PreviewId"] == "preview"
    assert path_of(transport.last) == "/v1/fxs/accounts/42/advisor/preview"
    assert query_of(transport.last) == {"kind": ["PLAN"], "syms": ["EURUSD", "XAUUSD"]}
    ask = a.send_advisor(42, {"ACNO": 42, "PreviewId": "preview", "Kind": "PLAN"}, idempotency_key="send-one")
    assert ask["Status"] == "QUEUED"
    assert (transport.last.method, path_of(transport.last)) == ("POST", "/v1/fxs/accounts/42/advisor/asks")
    assert json.loads(transport.last.body)["PreviewId"] == "preview"
    assert transport.last.headers["Idempotency-Key"] == "send-one"
    assert a.get_advisor_settings(42)["Balance"] == 30
    assert (transport.last.method, path_of(transport.last)) == ("GET", "/v1/fxs/accounts/42/advisor/settings")
    a.save_advisor_settings(42, {"ACNO": 42, "Settings": {"Enabled": True, "PaidReasks": False}},
                            idempotency_key="save-one")
    assert (transport.last.method, path_of(transport.last)) == ("PUT", "/v1/fxs/accounts/42/advisor/settings")
    assert json.loads(transport.last.body)["Settings"]["PaidReasks"] is False
    history = a.advisor_history(42, {"PageNo": 2, "PageSize": 10})
    assert history.items[0]["Refunded"] is True
    assert path_of(transport.last) == "/v1/fxs/accounts/42/advisor/asks"
    assert query_of(transport.last) == {"PageNo": ["2"], "PageSize": ["10"]}
    a.advisor_history_view(42, "ask/one", sent=True)
    assert urlsplit(transport.last.url).path.endswith("/advisor/asks/ask%2Fone")
    assert query_of(transport.last) == {"sent": ["true"]}
    decision = {"ACNO": 42, "Account": "DECLINE", "Pairs": [{"SYM": "EURUSD", "Decision": "ACCEPT"}]}
    a.decide_advisor(42, "ask/one", decision, idempotency_key="decide-one")
    assert transport.last.method == "PATCH"
    assert path_of(transport.last) == "/v1/fxs/accounts/42/advisor/asks/ask%2Fone"
    assert json.loads(transport.last.body)["Pairs"][0]["Decision"] == "ACCEPT"
    a.undo_advisor(42, {"ACNO": 42}, idempotency_key="undo-one")
    assert (transport.last.method, path_of(transport.last)) == ("POST", "/v1/fxs/accounts/42/advisor/undo")
    assert transport.last.headers["Idempotency-Key"] == "undo-one"
    a.get_aibot(42)
    assert path_of(transport.last).endswith("/42/aibot")
    a.aibot_decisions(42)
    assert path_of(transport.last).endswith("/42/aibot/decisions")
    assert all(r.headers["X-Api-Key"] == "copy-key" for r in transport.requests)


def test_advisor_preview_and_view_send_no_query_by_default(make_client: MakeClient) -> None:
    client, transport = make_client(envelope({"PreviewId": "preview"}))
    client.copy.accounts.preview_advisor(42)
    assert query_of(transport.last) == {}
    client.copy.accounts.advisor_history_view(42, "ask")
    assert query_of(transport.last) == {}
