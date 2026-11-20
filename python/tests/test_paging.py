from __future__ import annotations

from typing import List

from tests.conftest import MakeClient, envelope, path_of, query_of


def page(rows: List[int], number: int, count: int, **extra: object) -> object:
    return envelope(rows, paging={"pageNo": number, "pageSize": 2, "pageCount": count, "resCount": 5}, **extra)


def test_a_page_has_items_paging_and_the_symbols_not_found(make_client: MakeClient) -> None:
    fin, _ = make_client(page([1, 2], 1, 3, NotFound=["NOPE"]))
    result = fin.news.news.list(size=2, syms="A,B,NOPE")
    assert result.items == [1, 2] and list(result) == [1, 2] and len(result) == 2
    assert result.paging == {"pageNo": 1, "pageSize": 2, "pageCount": 3, "resCount": 5}
    assert result.not_found == ("NOPE",)


def test_a_page_with_every_symbol_found_has_none_missing(make_client: MakeClient) -> None:
    fin, _ = make_client(page([1], 1, 1))
    assert fin.news.news.list().not_found == ()


def test_pages_advance_page_until_the_last_page(make_client: MakeClient) -> None:
    fin, transport = make_client(page([1, 2], 1, 3), page([3, 4], 2, 3), page([5], 3, 3), page([9], 4, 3))
    pages = list(fin.news.news.list_pages(size=2, syms="AAPL"))
    assert [p.items for p in pages] == [[1, 2], [3, 4], [5]]
    assert [query_of(r)["page"] for r in transport.requests] == [["1"], ["2"], ["3"]]
    assert all(query_of(r)["syms"] == ["AAPL"] and query_of(r)["size"] == ["2"] for r in transport.requests)


def test_items_flatten_the_pages_and_stop_on_an_empty_page(make_client: MakeClient) -> None:
    fin, transport = make_client(page([1, 2], 1, 9), page([3], 2, 9), page([], 3, 9))
    assert list(fin.news.news.list_items(size=2)) == [1, 2, 3]
    assert len(transport.requests) == 3


def test_paging_can_start_after_page_one(make_client: MakeClient) -> None:
    fin, transport = make_client(page([5], 3, 3))
    assert list(fin.news.news.list_items(page=3)) == [5]
    assert query_of(transport.last)["page"] == ["3"]


def test_a_path_addressed_list_is_walked_with_its_path_argument(make_client: MakeClient) -> None:
    fin, transport = make_client(page([1, 2], 1, 2), page([3], 2, 2))
    assert list(fin.graph.screens.crypto_items("top gain", size=2)) == [1, 2, 3]
    assert {path_of(r) for r in transport.requests} == {"/api/screens/crypto/top%20gain"}
    assert [query_of(r)["page"] for r in transport.requests] == [["1"], ["2"]]
