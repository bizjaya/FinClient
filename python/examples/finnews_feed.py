"""Read the FinNews feed with filters, then page through it. Needs FINNEWS_API_KEY in the environment."""
import os

from finclient import FinClient, ServiceOptions

fin = FinClient(news=ServiceOptions(api_key=os.environ["FINNEWS_API_KEY"]))

# Plain keyword arguments: symbols, a sort (a minus sign sorts high to low), and the page to read.
page = fin.news.v1.news.list(syms="AAPL,MSFT", sort="-published", page=1, size=10)
print(page.paging)
for article in page:
    print(article.get("Title"))

# Every article that matches, page after page.
for article in fin.news.v1.news.list_items(syms="AAPL", size=50):
    print(article.get("Title"))
    break
