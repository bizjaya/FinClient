import { FinClient } from "../src/index.js";

const apiKey = process.env.FINCOPY_API_KEY;
if (!apiKey) throw new Error("Set FINCOPY_API_KEY in the server environment before running this example.");

const fin = new FinClient({ apiKey });
const accounts = await fin.copy.accounts.list();
console.log(`Accounts available: ${accounts.items.length}`);
for (const account of accounts.items) console.log(account.Fleet?.Account?.ACNO, account.Fleet?.Account?.Login);

// Each page ends with a cursor; a real application saves it and passes it back next time.
for await (const page of fin.copy.changes.readPages()) {
  for (const change of page.Changes ?? []) console.log(change.Entity, change.AccountId, change.OID, change.Status);
  console.log(`Next cursor: ${page.Cursor}`);
}

// This sends a real order when the key has Trade permission. Uncomment only when you intend to trade.
// await fin.copy.orders.make(123456, { SYM: "EURUSD", ACT: "NEW", OTY: "OP_BUY", LOT: 0.01 });
