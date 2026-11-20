"""Read what is being said about a few symbols, then the prediction markets. Needs FINSOCIAL_API_KEY."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(social=ServiceOptions(api_key=os.environ["FINSOCIAL_API_KEY"]))

# The pulse: mentions, engagement and the bullish and bearish split, for up to 50 symbols by name.
pulse = fin.social.pulse.pulse(syms="BTC,ETH")
for row in pulse:
    print(row["Sym"], row["Mentions"], row["Bullish"], row["Bearish"])
print("Not found:", pulse.not_found)  # names that no row carries

# /v1: included in paid plans. Every prediction market, a page at a time; markets_items follows the pages for you.
for market in fin.social.v1.markets.markets_items(size=25):
    print(market)
    break
