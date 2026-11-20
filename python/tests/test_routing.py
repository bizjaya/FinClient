from __future__ import annotations

import json

import pytest

from finclient import FinClient, MissingApiKeyError, ServiceOptions
from tests.conftest import MakeClient, envelope, host_of, path_of, query_of, reply


def test_each_service_uses_its_base_url_and_key_header(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list()
    fin.news.categories.flat()
    fin.social.markets.pulse_list()
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
    fin.news.categories.flat()
    fin.copy.accounts.list()
    assert host_of(transport.requests[0]) == "https://news.example.test"
    assert host_of(transport.requests[1]) == "https://copy.example.test"


def test_api_and_v1_operations_are_kept_apart(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list(limit=5)
    fin.graph.v1.tickers.list(limit=5)
    assert path_of(transport.requests[0]) == "/api/tickers"
    assert path_of(transport.requests[1]) == "/v1/tickers"
    assert query_of(transport.requests[1]) == {"limit": ["5"]}


def test_path_parameters_are_positional_and_encoded(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({}))
    fin.graph.tickers.view("BTC/USDT")
    fin.news.news.view("abc 123")
    assert path_of(transport.requests[0]) == "/api/tickers/BTC%2FUSDT"
    assert path_of(transport.requests[1]) == "/api/news/abc%20123"


def test_query_parameters_are_snake_case_keywords_with_wire_names(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]))
    fin.graph.tickers.list(dax="BIN", in_exchange=True, limit=10, offset=20)
    assert query_of(transport.last) == {
        "dax": ["BIN"], "inExchange": ["true"], "limit": ["10"], "offset": ["20"]}


def test_qry_is_one_json_parameter_from_keywords_or_a_dict(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([], paging={"pageNo": 1, "pageCount": 1}))
    fin.news.news.list(pageNo=2, pageSize=10, sortBy=[["published", "desc"]], Symbols="AAPL,MSFT")
    expected = {"pageNo": 2, "pageSize": 10, "sortBy": [["published", "desc"]], "Symbols": "AAPL,MSFT"}
    assert json.loads(query_of(transport.last)["qry"][0]) == expected

    fin.news.news.list({"pageNo": 2, "pageSize": 10}, Symbols="AAPL", Search=None)
    assert json.loads(query_of(transport.last)["qry"][0]) == {"pageNo": 2, "pageSize": 10, "Symbols": "AAPL"}

    fin.news.news.list(page_no=3, symbols="TSLA")  # snake_case spellings map to the wire names
    assert json.loads(query_of(transport.last)["qry"][0]) == {"pageNo": 3, "Symbols": "TSLA"}


def test_body_post_sends_json_and_no_idempotency_header(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({"Title": "x"}))
    result = fin.news.news.rating_make("abc", {"ReqSite": "demo", "ReqUsno": 7})
    request = transport.last
    assert (request.method, path_of(request)) == ("POST", "/api/news/abc/rating")
    assert json.loads(request.body or b"") == {"ReqSite": "demo", "ReqUsno": 7}
    assert request.headers["Content-Type"] == "application/json"
    assert "Idempotency-Key" not in request.headers
    assert result == {"Title": "x"}


def test_empty_body_is_sent_as_an_empty_object(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope({}))
    fin.news.news.rating_make("abc")
    assert json.loads(transport.last.body or b"") == {}


def test_missing_key_raises_at_first_use_before_any_request(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([]), news=None, graph=None)
    with pytest.raises(MissingApiKeyError) as raised:
        fin.news.categories.flat()
    assert "FinNews needs an API key" in str(raised.value)
    assert raised.value.status_code == 0
    assert transport.requests == []
    fin.social.markets.pulse_list()  # the other parts are unaffected


def test_operations_that_need_no_key_work_without_one(make_client: MakeClient) -> None:
    fin, transport = make_client(reply({"data": [], "count": 0}), graph=None)
    assert fin.graph.v1.assets.exchanges() == []
    assert "FinUsrKey" not in transport.last.headers


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
