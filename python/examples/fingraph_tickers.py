"""Read FinGraph markets and tickers. Needs FINGRAPH_API_KEY in the environment."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(graph=ServiceOptions(api_key=os.environ["FINGRAPH_API_KEY"]))

# /v1: included in paid plans. US stocks by name, up to 50 symbols; a minus sign sorts high to low.
stocks = fin.graph.v1.markets.eqt_usa(syms="NVDA,MSFT,BCDA", sort="-chg1d")
for stock in stocks:
    print(stock["Sym"], stock["Price"], stock["Signal"])
print("Not found:", stocks.not_found)  # names that no row carries

# Lists page with page and size; tickers_items follows the pages for you.
for index, ticker in enumerate(fin.graph.v1.tickers.list_items(exchange="BINANCE", quote="USDT", size=50)):
    print(ticker)
    if index >= 4:
        break

# /api: billed in credits per call, on any plan. Path parameters are positional.
print(fin.graph.tickers.view("BTCUSDT"))
