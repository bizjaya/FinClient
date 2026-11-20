from __future__ import annotations

import json
from typing import List

from tests.conftest import MakeClient, envelope, query_of


def qry_of(transport) -> dict:  # type: ignore[no-untyped-def]
    return json.loads(query_of(transport.last)["qry"][0])


def page(rows: List[int], number: int, count: int) -> object:
    return envelope(rows, paging={"pageNo": number, "pageSize": 2, "pageCount": count, "resCount": 5})


def test_a_page_has_items_and_paging(make_client: MakeClient) -> None:
    fin, _ = make_client(page([1, 2], 1, 3))
    result = fin.news.news.list(pageSize=2)
    assert result.items == [1, 2] and list(result) == [1, 2] and len(result) == 2
    assert result.paging == {"pageNo": 1, "pageSize": 2, "pageCount": 3, "resCount": 5}


def test_pages_advance_page_no_until_the_last_page(make_client: MakeClient) -> None:
    fin, transport = make_client(page([1, 2], 1, 3), page([3, 4], 2, 3), page([5], 3, 3), page([9], 4, 3))
    pages = list(fin.news.news.list_pages(pageSize=2, Symbols="AAPL"))
    assert [p.items for p in pages] == [[1, 2], [3, 4], [5]]
    assert [json.loads(query_of(r)["qry"][0])["pageNo"] for r in transport.requests] == [1, 2, 3]
    assert json.loads(query_of(transport.requests[2])["qry"][0])["Symbols"] == "AAPL"


def test_items_flatten_the_pages_and_stop_on_an_empty_page(make_client: MakeClient) -> None:
    fin, transport = make_client(page([1, 2], 1, 9), page([3], 2, 9), page([], 3, 9))
    assert list(fin.news.news.list_items(pageSize=2)) == [1, 2, 3]
    assert len(transport.requests) == 3


def test_paging_can_start_after_page_one(make_client: MakeClient) -> None:
    fin, transport = make_client(page([5], 3, 3))
    assert list(fin.news.news.list_items(pageNo=3)) == [5]
    assert qry_of(transport)["pageNo"] == 3


def test_limit_offset_paging_advances_offset(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([1, 2]), envelope([3, 4]), envelope([5]))
    assert list(fin.graph.tickers.list_items(limit=2, dax="BIN")) == [1, 2, 3, 4, 5]
    assert [query_of(r)["offset"] for r in transport.requests] == [["0"], ["2"], ["4"]]
    assert all(query_of(r)["limit"] == ["2"] and query_of(r)["dax"] == ["BIN"] for r in transport.requests)


def test_limit_offset_pages_stop_on_an_empty_page(make_client: MakeClient) -> None:
    fin, transport = make_client(envelope([1, 2]), envelope([]))
    assert [len(rows) for rows in fin.graph.v1.tickers.list_pages(limit=2)] == [2]
    assert len(transport.requests) == 2
