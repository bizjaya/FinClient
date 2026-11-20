"""Read FinSocial trends and prediction markets. Needs FINSOCIAL_API_KEY in the environment."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(social=ServiceOptions(api_key=os.environ["FINSOCIAL_API_KEY"]))

print(fin.social.v1.social.trending_list(window="24h", limit=10))

for market in fin.social.v1.markets.market_list_items(pageSize=25):
    print(market)
    break
