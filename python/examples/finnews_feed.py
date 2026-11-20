"""Read the FinNews feed with filters, then page through it. Needs FINNEWS_API_KEY in the environment."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(news=ServiceOptions(api_key=os.environ["FINNEWS_API_KEY"]))

# `qry` is sent as one JSON parameter. Keyword arguments keep the API's own field names.
page = fin.news.v1.news.all(
    pageNo=1,
    pageSize=10,
    sortBy=[["published", "desc"]],
    Symbols="AAPL,MSFT",
    StmGte="Positive",
)
print(page.paging)
for article in page:
    print(article.get("Title"))

# Every article that matches, page after page.
for article in fin.news.v1.news.all_items(pageSize=50, Symbols="AAPL"):
    print(article.get("Title"))
    break

# POST: ask for an AI rating of one article.
# rating = fin.news.news.rating_make("<checksum>", {"ReqSite": "my-site", "ReqUsno": 1})
