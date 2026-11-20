from __future__ import annotations

import json
from datetime import datetime

import pytest

from finclient import FinClient, MissingApiKeyError, ServiceOptions
from tests.conftest import MakeClient, envelope, host_of, path_of, query_of


def test_each_service_uses_its_base_url_and_key_header(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list()
    fin.news.categories.list()
    fin.social.pulse.pulse()
    fin.copy.accounts.list()
    graph, news, social, copy = transport.requests
    assert (host_of(graph), graph.headers["FinUsrKey"]) == ("https://x.fingraph.io", "graph-key")
    assert (host_of(news), news.headers["FinUsrKey"]) == ("https://x.finnews.io", "news-key")
    assert (host_of(social), social.headers["FinUsrKey"]) == ("https://x.finsocial.io", "social-key")
    assert (host_of(copy), copy.headers["X-Api-Key"]) == ("https://api.fincopy.io", "copy-key")
    assert "FinUsrKey" not in copy.headers and "X-Api-Key" not in graph.headers


def test_base_urls_can_be_overridden_per_service(make_client: MakeClient) -> None:
    fin, transport = make_client(
        envelope([]),
        base_url="https://copy.example.test/",
        news=ServiceOptions(api_key="n", base_url="https://news.example.test"),
    )
    fin.news.categories.list()
    fin.copy.accounts.list()
    assert host_of(transport.requests[0]) == "https://news.example.test"
    assert host_of(transport.requests[1]) == "https://copy.example.test"


def test_api_and_v1_operations_are_kept_apart(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list(size=5)
    fin.graph.v1.tickers.list(size=5)
    assert path_of(transport.requests[0]) == "/api/tickers"
    assert path_of(transport.requests[1]) == "/v1/tickers"
    assert query_of(transport.requests[1]) == {"size": ["5"]}


def test_path_parameters_are_positional_and_encoded(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({}))
    fin.graph.tickers.view("BTC/USDT")
    fin.news.news.view("abc 123")
    assert path_of(transport.requests[0]) == "/api/tickers/BTC%2FUSDT"
    assert path_of(transport.requests[1]) == "/api/news/abc%20123"


def test_query_parameters_are_snake_case_keywords_with_wire_names(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list(exchange="BINANCE", in_exchange=True, size=10, page=2)
    assert query_of(transport.last) == {
        "exchange": ["BINANCE"], "inExchange": ["true"], "size": ["10"], "page": ["2"]}


def test_list_filters_are_plain_query_values_and_no_qry_parameter(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([], paging={"pageNo": 1, "pageCount": 1}))
    fin.news.news.list(syms="AAPL,MSFT", sort="-published", page=2, size=10, q=None)
    assert query_of(transport.last) == {
        "syms": ["AAPL,MSFT"], "sort": ["-published"], "page": ["2"], "size": ["10"]}
    fin.social.v1.posts.posts(syms="BTC", stance="bull")
    assert "qry" not in query_of(transport.last)


def test_a_plain_service_sends_a_json_body_and_no_idempotency_header(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({"Title": "x"}))
    result = fin.news.news._service.request("POST", "/api/news/abc/rating", body={"ReqSite": "demo", "ReqUsno": 7})
    request = transport.last
    assert (request.method, path_of(request)) == ("POST", "/api/news/abc/rating")
    assert json.loads(request.body or b"") == {"ReqSite": "demo", "ReqUsno": 7}
    assert request.headers["Content-Type"] == "application/json"
    assert "Idempotency-Key" not in request.headers
    assert result["data"] == {"Title": "x"}


def test_a_date_query_value_is_sent_as_iso_8601(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.news.news.list(since=datetime(2026, 11, 1, 8, 0, 0))
    assert query_of(transport.last) == {"since": ["2026-11-01T08:00:00"]}


def test_missing_key_raises_at_first_use_before_any_request(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]), news=None, graph=None)
    with pytest.raises(MissingApiKeyError) as raised:
        fin.news.categories.list()
    assert "FinNews needs an API key" in str(raised.value)
    assert raised.value.status_code == 0
    assert transport.requests == []
    fin.social.pulse.pulse()  # the other parts are unaffected


def test_a_v1_operation_needs_a_key_too(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]), graph=None)
    with pytest.raises(MissingApiKeyError):
        fin.graph.v1.assets.exchanges()
    assert transport.requests == []


def test_client_rejects_bad_options() -> None:
    with pytest.raises(ValueError):
        FinClient(api_key="k", base_url="ftp://example.test")
    with pytest.raises(ValueError):
        FinClient(api_key="k", max_retries=-1)
    with pytest.raises(ValueError):
        FinClient(api_key="k", timeout=0)


def test_keys_are_never_shown_in_reprs() -> None:
    options = ServiceOptions(api_key="secret-value")
    assert "secret-value" not in repr(options)
