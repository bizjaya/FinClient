"""Read FinGraph tickers and signals. Needs FINGRAPH_API_KEY in the environment."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(graph=ServiceOptions(api_key=os.environ["FINGRAPH_API_KEY"]))

# /v1: included in paid plans. Rows are paged by limit and offset; list_items follows the pages for you.
for index, ticker in enumerate(fin.graph.v1.tickers.list_items(dax="BIN", quote="USDT", limit=100)):
    print(ticker)
    if index >= 4:
        break

# /api: billed in credits per call, on any plan. Path parameters are positional.
print(fin.graph.tickers.view("BTCUSDT"))
print(fin.graph.signals.view("BTCUSDT"))
