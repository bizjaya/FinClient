from __future__ import annotations

import hashlib
import hmac
import json
import time

import pytest

from finclient import (
    ApiConnectionError,
    ApiResponseError,
    FinApiError,
    FinWebhook,
    StreamResponse,
    TransportError,
    verify_signature,
)
from tests.conftest import MakeClient, envelope, path_of, query_of, reply

NOW = 1_792_430_400


def sign(secret: str, timestamp: str, body: str) -> str:
    return hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256).hexdigest()


# -- webhook signature: the same vectors as the TypeScript tests -------------------------------------------------------


def test_webhook_accepts_a_valid_signature_and_refuses_a_changed_body() -> None:
    timestamp = str(NOW)
    body = '{"Order":{"OID":42}}'
    signature = sign("webhook-secret", timestamp, body)
    assert verify_signature("webhook-secret", timestamp, body, f"sha256={signature}", now=NOW)
    assert not verify_signature("webhook-secret", timestamp, body + " ", signature, now=NOW)
    assert not verify_signature("other-secret", timestamp, body, signature, now=NOW)


def test_webhook_refuses_a_signature_older_than_five_minutes() -> None:
    timestamp = str(NOW - 301)
    assert not FinWebhook.verify("webhook-secret", timestamp, "{}", sign("webhook-secret", timestamp, "{}"), now=NOW)
    timestamp = str(NOW - 300)
    assert FinWebhook.verify("webhook-secret", timestamp, "{}", sign("webhook-secret", timestamp, "{}"), now=NOW)


def test_webhook_refuses_a_timestamp_more_than_five_minutes_ahead() -> None:
    timestamp = str(NOW + 301)
    assert not verify_signature("s", timestamp, "{}", sign("s", timestamp, "{}"), now=NOW)


def test_webhook_refuses_malformed_input() -> None:
    assert not verify_signature("s", str(NOW), "{}", "sha256=abc", now=NOW)
    assert not verify_signature("s", str(NOW), "{}", "not-hex" * 10, now=NOW)
    assert not verify_signature("s", "12ab", "{}", sign("s", "12ab", "{}"), now=NOW)
    assert not verify_signature("", str(NOW), "{}", sign("", str(NOW), "{}"), now=NOW)
    assert not verify_signature("s", str(NOW), "{}", "", now=NOW)


def test_webhook_verifies_a_byte_body_and_an_integer_timestamp() -> None:
    assert verify_signature("s", NOW, b"{}", sign("s", str(NOW), "{}"), now=NOW)


def test_webhook_uses_the_clock_by_default(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(time, "time", lambda: float(NOW))
    assert verify_signature("s", str(NOW), "{}", sign("s", str(NOW), "{}"))


# -- resources ---------------------------------------------------------------------------------------------------------


def test_accounts_list_sends_page_and_filter_parameters(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([{"ACNO": 1}], paging={"pageNo": 1, "pageCount": 1}))
    page = fin.copy.accounts.list({"PageNo": 2, "PageSize": 5, "Search": "x", "SortBy": [["ACNO", "desc"]]}, "ref-1")
    assert page.items == [{"ACNO": 1}]
    assert path_of(transport.last) == "/v1/fxs/accounts"
    assert query_of(transport.last) == {
        "PageNo": ["2"], "PageSize": ["5"], "search": ["x"], "SortBy[0][0]": ["ACNO"], "SortBy[0][1]": ["desc"],
        "externalRef": ["ref-1"],
    }
    assert transport.last.headers["X-Api-Key"] == "copy-key"
    assert "Idempotency-Key" not in transport.last.headers


def test_accounts_pages_advance_page_no(make_client: MakeClient) -> None:
    fin, transport = make_client(
        envelope([1, 2], paging={"pageNo": 1, "pageSize": 2, "pageCount": 2}),
        envelope([3], paging={"pageNo": 2, "pageSize": 2, "pageCount": 2}),
    )
    assert list(fin.copy.accounts.list_items({"PageSize": 2})) == [1, 2, 3]
    assert [query_of(r)["PageNo"] for r in transport.requests] == [["1"], ["2"]]


def test_a_write_carries_a_fresh_idempotency_key_and_drops_null_fields(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({"Queued": True}))
    fin.copy.orders.make(52014125, {"SYM": "EURUSD", "ACT": "NEW", "OTY": "OP_BUY", "LOT": 0.01, "TID": None})
    first = transport.last
    assert (first.method, path_of(first)) == ("POST", "/v1/fxs/accounts/52014125/trade")
    assert json.loads(first.body or b"") == {"SYM": "EURUSD", "ACT": "NEW", "OTY": "OP_BUY", "LOT": 0.01}
    fin.copy.orders.make(52014125, {"SYM": "EURUSD"})
    assert first.headers["Idempotency-Key"] != transport.last.headers["Idempotency-Key"]
    fin.copy.orders.make(52014125, {"SYM": "EURUSD"}, idempotency_key="my-key")
    assert transport.last.headers["Idempotency-Key"] == "my-key"


def test_a_write_is_retried_on_5xx_with_the_same_key(make_client: MakeClient) -> None:
    fin, transport = make_client(reply({"message": "x"}, status=503), envelope({"Queued": True}))
    fin.copy.orders.make(1, {"SYM": "EURUSD"})
    assert len(transport.requests) == 2
    assert transport.requests[0].headers["Idempotency-Key"] == transport.requests[1].headers["Idempotency-Key"]


def test_arguments_are_validated_before_sending(make_client: MakeClient) -> None:
    fin, transport = make_client()
    with pytest.raises(ValueError):
        fin.copy.accounts.get(0)
    with pytest.raises(ValueError):
        fin.copy.accounts.pair(1, -1)
    with pytest.raises(ValueError):
        fin.copy.orders.make(1, {}, idempotency_key="x" * 121)
    with pytest.raises(ValueError):
        fin.copy.changes.get(take=0)
    assert transport.requests == []


def test_null_data_is_an_empty_response_error(make_client: MakeClient) -> None:
    fin, _ = make_client(reply({"data": None, "isOk": True, "code": 200}))
    with pytest.raises(ApiResponseError) as raised:
        fin.copy.accounts.get(1)
    assert raised.value.error_code == "empty_response"


def test_performance_dates_and_booleans(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({}))
    fin.copy.fx.performance_bars(demo=True, from_dt="2026-01-01T00:00:00Z")
    assert query_of(transport.last) == {"demo": ["true"], "fromDt": ["2026-01-01T00:00:00Z"]}


def test_remove_sends_a_delete_with_a_body(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({"Queued": True}))
    fin.copy.accounts.remove(5, {"TrPin": "1234"})
    assert (transport.last.method, json.loads(transport.last.body or b"")) == ("DELETE", {"TrPin": "1234"})


def test_webhook_configuration(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope(True))
    assert fin.copy.webhooks.delete() is True
    assert (transport.last.method, path_of(transport.last)) == ("DELETE", "/v1/webhook")


# -- change feed -------------------------------------------------------------------------------------------------------


def feed(changes: list, cursor: str, more: bool):  # type: ignore[no-untyped-def]
    return envelope({"Changes": changes, "Cursor": cursor, "More": more})


def test_change_feed_follows_the_cursor(make_client: MakeClient) -> None:
    fin, transport = make_client(feed([{"Id": 1}], "c1", True), feed([{"Id": 2}], "c2", False))
    assert [c["Id"] for c in fin.copy.changes.read_all(take=50)] == [1, 2]  # type: ignore[typeddict-item]
    assert "cursor" not in query_of(transport.requests[0])
    assert query_of(transport.requests[1])["cursor"] == ["c1"] and query_of(transport.requests[1])["take"] == ["50"]


def test_change_feed_refuses_more_without_a_new_cursor(make_client: MakeClient) -> None:
    fin, _ = make_client(feed([], "c1", True))
    with pytest.raises(ApiResponseError):
        list(fin.copy.changes.read_pages("c1"))


# -- event stream ------------------------------------------------------------------------------------------------------


def test_stream_yields_events(make_client: MakeClient) -> None:
    fin, transport = make_client()
    transport.stream_lines = [b": ping", b"event: order", b"data: {\"a\":1}", b"", b"data: one", b"data: two", b"",
                              b"event: last", b"data: tail"]
    events = list(fin.copy.fx.stream())
    assert [(e.event, e.data) for e in events] == [("order", '{"a":1}'), ("message", "one\ntwo"), ("last", "tail")]
    assert path_of(transport.last) == "/v1/fxs/stream"
    assert transport.last.headers["Accept"] == "text/event-stream"


def test_stream_that_breaks_midway_is_a_network_error(make_client: MakeClient) -> None:
    fin, transport = make_client()

    def broken():  # type: ignore[no-untyped-def]
        yield b"data: x"
        yield b""
        raise TransportError("reset")

    transport.stream = lambda request: StreamResponse(200, {}, broken())  # type: ignore[method-assign]
    seen = []
    with pytest.raises(ApiConnectionError):
        for event in fin.copy.fx.stream():
            seen.append(event.data)
    assert seen == ["x"]


def test_stream_error_status_is_mapped(make_client: MakeClient) -> None:
    fin, transport = make_client()
    transport.stream = lambda request: StreamResponse(  # type: ignore[method-assign]
        401, {}, iter(()), b'{"isOk":false,"message":"No key."}')
    with pytest.raises(FinApiError) as raised:
        list(fin.copy.fx.stream())
    assert raised.value.status_code == 401
