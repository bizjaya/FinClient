from __future__ import annotations

import json

from tests.conftest import MakeClient, envelope, path_of, query_of


def test_advisor_routes_preserve_owner_account_commands_and_idempotency(make_client: MakeClient) -> None:
    client, transport = make_client(envelope({"Id": "preview"}))
    a = client.copy.accounts
    assert a.preview_advisor(42, "PLAN")["Id"] == "preview"
    assert query_of(transport.last)["kind"] == ["PLAN"]
    a.send_advisor(42, {"ACNO": 42, "PreviewId": "preview", "Kind": "PLAN"}, idempotency_key="send-one")
    assert path_of(transport.last) == "/v1/fxs/accounts/42/aibot/advisor/send"
    assert json.loads(transport.last.body)["PreviewId"] == "preview"
    assert transport.last.headers["Idempotency-Key"] == "send-one"
    a.save_advisor_settings(42, {"ACNO": 42, "Settings": {"PaidReasks": False}}, idempotency_key="save-one")
    assert transport.last.method == "PUT"
    assert json.loads(transport.last.body)["Settings"]["PaidReasks"] is False
    a.advisor_history(42)
    assert path_of(transport.last).endswith("/42/aibot/advisor/history")
    a.advisor_history_view(42, "ask/one")
    assert path_of(transport.last).endswith("/history/ask%2Fone")
    a.decide_aibot_advice(42, {"ACNO": 42, "AdviceId": "ask", "Accept": False}, idempotency_key="decline-one")
    assert path_of(transport.last).endswith("/advice/decide")
    a.undo_aibot_advice(42, {"ACNO": 42}, idempotency_key="undo-one")
    assert transport.last.headers["Idempotency-Key"] == "undo-one"
    a.get_aibot(42)
    assert path_of(transport.last).endswith("/42/aibot")
    a.aibot_decisions(42)
    assert path_of(transport.last).endswith("/42/aibot/decisions")
    assert all(r.headers["X-Api-Key"] == "copy-key" for r in transport.requests)
