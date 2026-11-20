from __future__ import annotations

import pytest

from finclient import (
    ApiConnectionError,
    ApiResponseError,
    AuthenticationError,
    FinApiError,
    InsufficientCreditsError,
    NotFoundError,
    PermissionDeniedError,
    RateLimitError,
    ServerError,
    TransportError,
)
from tests.conftest import MakeClient, Sleeps, envelope, reply


@pytest.mark.parametrize(
    ("status", "error_class", "code"),
    [
        (401, AuthenticationError, "invalid_api_key"),
        (402, InsufficientCreditsError, "insufficient_credits"),
        (403, PermissionDeniedError, "package_not_allowed"),
        (404, NotFoundError, "not_found"),
        (500, ServerError, "boom"),
    ],
)
def test_statuses_map_to_the_exception_hierarchy(
    make_client: MakeClient, status: int, error_class: type, code: str
) -> None:
    fin, transport = make_client(reply({"error": code, "message": "Nope."}, status=status))
    with pytest.raises(error_class) as raised:
        fin.news.categories.flat()
    error = raised.value
    assert isinstance(error, FinApiError)
    assert (error.status_code, error.code, error.message) == (status, code, "Nope.")
    assert len(transport.requests) == 1  # none of these is retried


def test_402_carries_the_limit_fields(make_client: MakeClient) -> None:
    body = {"error": "insufficient_credits", "message": "Out of credits.", "lane": "FgDaily", "limit": 100,
            "catag": "ohlcv", "tier": "2"}
    fin, _ = make_client(reply(body, status=402))
    with pytest.raises(InsufficientCreditsError) as raised:
        fin.graph.tickers.list()
    error = raised.value
    assert (error.lane, error.limit, error.catag, error.tier) == ("FgDaily", 100, "ohlcv", "2")


def test_nested_error_bodies_and_envelopes_are_read_too(make_client: MakeClient) -> None:
    fin, _ = make_client(reply({"error": {"code": "bad_request", "message": "Bad symbol."}}, status=400))
    with pytest.raises(FinApiError) as raised:
        fin.graph.v1.tickers.get("??")
    assert (raised.value.status_code, raised.value.code, raised.value.message) == (400, "bad_request", "Bad symbol.")

    fin, _ = make_client(reply({"isOk": False, "code": 7, "message": "Refused.", "errors": ["a", "b"]}, status=400))
    with pytest.raises(FinApiError) as raised:
        fin.copy.accounts.get(1)
    assert (raised.value.code, raised.value.errors) == (7, ("a", "b"))


def test_429_waits_for_retry_after_then_succeeds(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, transport = make_client(
        reply({"error": "rate_limited", "message": "Slow down."}, status=429, headers={"retry-after": "7"}),
        envelope([{"Slug": "x"}]),
    )
    assert fin.news.categories.flat() == [{"Slug": "x"}]
    assert len(transport.requests) == 2
    assert sleeps.waits == [7.0]


def test_429_without_retry_after_backs_off_in_whole_seconds(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, _ = make_client(reply({"error": "rate_limited", "message": "x"}, status=429), envelope([]))
    fin.news.categories.flat()
    assert sleeps.waits == [1.0]


def test_429_gives_up_after_max_retries_and_exposes_retry_after(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, transport = make_client(
        reply({"error": "rate_limited", "message": "Slow."}, status=429, headers={"Retry-After": "2"}),
        max_retries=2,
    )
    with pytest.raises(RateLimitError) as raised:
        fin.news.categories.flat()
    assert len(transport.requests) == 3
    assert raised.value.retry_after == 2.0
    assert raised.value.error_code == "rate_limited"
    assert sleeps.waits == [2.0, 2.0]


def test_a_long_retry_after_is_not_slept_through(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, transport = make_client(reply({"error": "rate_limited", "message": "x"}, status=429,
                                       headers={"Retry-After": "3600"}))
    with pytest.raises(RateLimitError) as raised:
        fin.news.categories.flat()
    assert len(transport.requests) == 1 and sleeps.waits == []
    assert raised.value.retry_after == 3600.0


def test_5xx_is_retried_for_a_get(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, transport = make_client(reply({"message": "bad gateway"}, status=502), reply(None, status=503), envelope([]))
    assert fin.graph.tickers.list() == []
    assert len(transport.requests) == 3
    assert sleeps.waits == [0.25, 0.5]  # 0.25 * 2**n * (0.75 + 0.5 * 0.5)


def test_5xx_is_not_retried_for_a_post_without_an_idempotency_key(make_client: MakeClient) -> None:
    fin, transport = make_client(reply({"error": "unavailable", "message": "x"}, status=503))
    with pytest.raises(ServerError):
        fin.news.news.rating_make("abc")
    assert len(transport.requests) == 1


def test_a_429_is_retried_even_for_a_post(make_client: MakeClient, sleeps: Sleeps) -> None:
    fin, transport = make_client(reply({"error": "rate_limited", "message": "x"}, status=429,
                                       headers={"Retry-After": "1"}), envelope({"Title": "t"}))
    assert fin.news.news.rating_make("abc") == {"Title": "t"}
    assert len(transport.requests) == 2


def test_network_failure_and_timeout(make_client: MakeClient) -> None:
    fin, transport = make_client(TransportError("refused"), max_retries=1)
    with pytest.raises(ApiConnectionError) as raised:
        fin.graph.tickers.list()
    assert (raised.value.error_code, raised.value.status_code) == ("network", 0)
    assert len(transport.requests) == 2

    fin, _ = make_client(TransportError("slow", timeout=True), max_retries=0)
    with pytest.raises(ApiConnectionError) as raised:
        fin.graph.tickers.list()
    assert raised.value.error_code == "timeout"


def test_an_unusable_success_body_is_an_error(make_client: MakeClient) -> None:
    fin, _ = make_client(reply(None))
    with pytest.raises(ApiResponseError):
        fin.graph.tickers.list()
    fin, _ = make_client(reply({"isOk": False, "message": "No."}, status=200))
    with pytest.raises(FinApiError):
        fin.graph.tickers.list()
