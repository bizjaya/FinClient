"""The default transport against a local server, and facts about the package itself."""
from __future__ import annotations

import json
import re
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from typing import Any, Dict, Iterator, List

import pytest

import finclient
from finclient import ApiConnectionError, FinClient, HttpRequest, ServerError, ServiceOptions, UrllibTransport

ROOT = Path(__file__).resolve().parent.parent


class Handler(BaseHTTPRequestHandler):
    seen: List[Dict[str, Any]] = []

    def do_GET(self) -> None:  # noqa: N802
        self.seen.append({"path": self.path, "key": self.headers.get("FinUsrKey")})
        if "REDIR" in self.path:
            self.send_response(302)
            self.send_header("Location", "http://127.0.0.1:1/elsewhere")
            self.end_headers()
            return
        status = 500 if "FAIL" in self.path else 200
        body = json.dumps({"error": "boom", "message": "Broken."} if status == 500 else {"data": [1, 2]}).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args: Any) -> None:
        return None


@pytest.fixture
def server() -> Iterator[str]:
    Handler.seen = []
    httpd = HTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{httpd.server_port}"
    finally:
        httpd.shutdown()
        httpd.server_close()
        thread.join(timeout=5)


def test_default_transport_sends_the_key_header_and_reads_json(server: str) -> None:
    fin = FinClient(graph=ServiceOptions(api_key="graph-key", base_url=server))
    assert fin.graph.tickers.list().items == [1, 2]
    assert Handler.seen[0]["key"] == "graph-key"


def test_default_transport_maps_error_statuses(server: str) -> None:
    raw = UrllibTransport().send(HttpRequest("GET", f"{server}/api/tickers?exchange=FAIL", {}, None, 5.0))
    assert raw.status == 500 and json.loads(raw.body)["error"] == "boom"
    fin = FinClient(graph=ServiceOptions(api_key="k", base_url=server), max_retries=0)
    with pytest.raises(ServerError) as raised:
        fin.graph.tickers.list(exchange="FAIL")
    assert raised.value.code == "boom"


def test_redirects_are_not_followed_so_a_key_never_leaves_the_host(server: str) -> None:
    fin = FinClient(graph=ServiceOptions(api_key="k", base_url=server), max_retries=0)
    with pytest.raises(finclient.FinApiError) as raised:
        fin.graph.tickers.list(exchange="REDIR")
    assert raised.value.status_code == 302
    assert len(Handler.seen) == 1


def test_default_transport_reports_a_refused_connection() -> None:
    fin = FinClient(graph=ServiceOptions(api_key="k", base_url="http://127.0.0.1:1"), max_retries=0, timeout=2)
    with pytest.raises(ApiConnectionError) as raised:
        fin.graph.tickers.list()
    assert raised.value.error_code in ("network", "timeout")


def test_version_matches_pyproject() -> None:
    text = (ROOT / "pyproject.toml").read_text(encoding="utf-8")
    match = re.search(r'^version = "([^"]+)"', text, re.MULTILINE)
    assert match and match.group(1) == finclient.__version__


def test_package_is_typed() -> None:
    assert (Path(finclient.__file__).parent / "py.typed").exists()
