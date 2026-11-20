"""List your FinCopy accounts and their open orders. Needs FINCOPY_API_KEY in the environment."""
import os

from finclient import FinClient, InsufficientCreditsError, RateLimitError

fin = FinClient(api_key=os.environ["FINCOPY_API_KEY"])

try:
    for account in fin.copy.accounts.list_items({"PageSize": 20}):
        fleet = (account.get("Fleet") or {}).get("Account") or {}
        print(fleet.get("ACNO"), fleet.get("Login"))
    for order in fin.copy.fx.orders_items({"PageSize": 50}):
        print("open order", order.get("OID"), order.get("SYM"))
except RateLimitError as error:
    print(f"Rate limited; retry in {error.retry_after} seconds.")
except InsufficientCreditsError as error:
    print(f"Out of credits: {error.message}")

# Queue a trade. A successful call means the work is queued for the terminal, not that it has filled.
# fin.copy.orders.make(52014125, {"SYM": "EURUSD", "ACT": "NEW", "OTY": "OP_BUY", "LOT": 0.01})
