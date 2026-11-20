# FinClient for Python

One typed Python client for four APIs: **FinCopy** (trading accounts and orders), **FinGraph** (prices, tickers and
signals), **FinNews** (news, sentiment and videos) and **FinSocial** (social attention and prediction markets).

- Python 3.10 or newer. No runtime dependencies: HTTP uses the standard library.
- Fully typed (`py.typed`). Request and response shapes are `TypedDict`s generated from each service's OpenAPI document.
- Retries with backoff, `Retry-After`, one exception hierarchy, and iterators over pages.

```sh
pip install finclient
```

## Keys

Each service has its own key. Build the client with the keys you have; a part used without its key raises
`MissingApiKeyError` the first time you call it, before any request is sent.

```python
import os
from finclient import FinClient, ServiceOptions

fin = FinClient(
    api_key=os.environ["FINCOPY_API_KEY"],                            # FinCopy, sent as X-Api-Key
    graph=ServiceOptions(api_key=os.environ["FINGRAPH_API_KEY"]),     # FinGraph, sent as FinUsrKey
    news=ServiceOptions(api_key=os.environ["FINNEWS_API_KEY"]),       # FinNews, sent as FinUsrKey
    social=ServiceOptions(api_key=os.environ["FINSOCIAL_API_KEY"]),   # FinSocial, sent as FinUsrKey
)
```

Keep keys in trusted server-side code and in the environment or a secret manager, never in source control.

| Part | Reached as | Default address | Key header |
|---|---|---|---|
| FinCopy | `fin.copy` | `https://api.fincopy.io` | `X-Api-Key` |
| FinGraph | `fin.graph` | `https://x.fingraph.io` | `FinUsrKey` |
| FinNews | `fin.news` | `https://x.finnews.io` | `FinUsrKey` |
| FinSocial | `fin.social` | `https://x.finsocial.io` | `FinUsrKey` |

`ServiceOptions` also takes `base_url`, `timeout` and `max_retries` for one part; `FinClient(timeout=30, max_retries=3)`
sets them for all. The top-level `api_key` and `base_url` are FinCopy's.

## FinCopy

The surface mirrors the TypeScript and .NET clients, in snake_case: `fin.copy.accounts`, `.orders`, `.fx`, `.changes` and
`.webhooks`. Fields keep the API's own names (`ACNO`, `SYM`, `LOT`, ...).

```python
for account in fin.copy.accounts.list_items({"PageSize": 20}):
    print(account["Fleet"]["Account"]["ACNO"])

# A trade call returns once the work is queued for the terminal; follow the stream, the change feed or a webhook.
fin.copy.orders.make(52014125, {"SYM": "EURUSD", "ACT": "NEW", "OTY": "OP_BUY", "LOT": 0.01})

for change in fin.copy.changes.read_all():          # follows the cursor
    print(change)
for event in fin.copy.fx.stream():                  # live events; reconnect in a loop when it ends
    print(event.event, event.data)
```

Writes carry an `Idempotency-Key` (a fresh one, or pass `idempotency_key=`), so a retried write cannot apply twice.

### Webhooks

```python
from finclient import verify_signature

# `raw_body` is the exact request body; `headers` the request headers.
ok = verify_signature(secret, headers["X-FinCopy-Timestamp"], raw_body, headers["X-FinCopy-Signature"])
```

The signature is an HMAC-SHA256 over `<timestamp>.<body>`. A timestamp more than five minutes from now, in either
direction, is refused.

## FinGraph, FinNews and FinSocial: two surfaces

Every operation of these three services is reachable, in two kept-apart surfaces:

| Surface | Reached as | Billing |
|---|---|---|
| `/api/...` | `fin.graph.<group>.<operation>()` | Credits per call, on any plan. |
| `/v1/...` | `fin.graph.v1.<group>.<operation>()` | Included in paid plans and gated by package tier. A few are billed per call like `/api`; some currently need no key. |

`<group>` is the operation's tag in snake_case (`tickers`, `news`, `sig_dax`) and `<operation>` the action in snake_case
(`List` is `list`, `RatingMake` is `rating_make`). Each method's docstring says its route, its billing, and its
parameters, taken from the API description, so `help(fin.news.v1.news.all)` is the reference.

```python
fin.graph.tickers.view("BTCUSDT")                       # GET /api/tickers/BTCUSDT   (credits per call)
fin.graph.v1.tickers.list(dax="BIN", limit=50)          # GET /v1/tickers?dax=BIN&limit=50
fin.news.news.rating_make("<checksum>", {"ReqSite": "my-site", "ReqUsno": 1})   # POST with a body
fin.social.v1.social.trending_list(window="24h")
```

Path parameters are positional, other query parameters are keyword arguments in snake_case (`in_exchange=True` sends
`inExchange=true`), and a request body is the last positional argument.

### Filters: the `qry` parameter

List endpoints that filter and page take a single `qry` value, a JSON object with `pageNo`, `pageSize`, `sortBy`,
`srchBy` and the filter fields of that endpoint. Pass it as keyword arguments, as a dict, or both. Names are the API's own
(`pageNo`, `Symbols`); snake_case spellings (`page_no`, `symbols`) are accepted too.

```python
page = fin.news.v1.news.all(
    pageNo=1, pageSize=20, sortBy=[["published", "desc"]],
    Symbols="AAPL,MSFT", StmGte="Positive",
)
page.items      # the rows (iterating the page yields them too)
page.paging     # {"pageNo": 1, "pageSize": 20, "pageCount": 7, "resCount": 131}

fin.news.v1.news.all({"pageSize": 20}, Symbols="AAPL")      # a dict and keywords merge
```

### Paging

Every operation that pages has two iterators beside it: `<operation>_pages` and `<operation>_items`.

```python
for page in fin.news.v1.news.all_pages(pageSize=50, Symbols="AAPL"):      # one Page per request
    print(page.paging["pageNo"], len(page))

for article in fin.news.v1.news.all_items(pageSize=50):                   # every row, page after page
    print(article["Title"])

for ticker in fin.graph.v1.tickers.list_items(dax="BIN", limit=100):      # limit/offset endpoints
    print(ticker["Symbol"])
```

`qry` operations advance `pageNo` until the last page (or an empty one). FinGraph's limit/offset lists advance `offset`
by the rows returned and stop at a short or empty page. FinCopy lists have the same helpers: `accounts.list_items()`,
`fx.orders_pages()`, and so on. Every page is one billed call.

## Errors and retries

Every failure is a `FinApiError`:

| Exception | When |
|---|---|
| `AuthenticationError` | 401, or `MissingApiKeyError` when no key was given |
| `InsufficientCreditsError` | 402 |
| `PermissionDeniedError` | 403 |
| `NotFoundError` | 404 |
| `RateLimitError` | 429 |
| `ServerError` | 5xx |
| `ApiConnectionError` | timeout or network failure (`error_code` is `timeout` or `network`) |
| `ApiResponseError` | an answer that is not a usable reply |

An error carries `status_code`, `code` (the API's own code such as `insufficient_credits`), `message`, `errors`, and, on limit
refusals, `lane`, `limit`, `catag` and `tier`. A `RateLimitError` has `retry_after` in seconds when the API said.

```python
from finclient import InsufficientCreditsError, RateLimitError

try:
    fin.graph.tickers.list()
except InsufficientCreditsError as error:
    print(error.code, error.limit, error.tier)
except RateLimitError as error:
    print("try again in", error.retry_after)
```

A `429` is retried (waiting for `Retry-After` when it is 60 seconds or less), and so are `502`, `503` and `504`, up to
`max_retries` (default 3) with exponential backoff. A request that is not safe to repeat (a `POST` without an
idempotency key) is retried only on `429`, which the service refused before processing. Redirects are never followed, so
a key cannot be forwarded to another host.

## Your own transport

`FinClient(transport=...)` takes any object with `send(HttpRequest) -> HttpResponse` and
`stream(HttpRequest) -> StreamResponse`, for a proxy, extra headers, or tests.

## License

MIT. Copyright BizJaya TechLab S/B.
