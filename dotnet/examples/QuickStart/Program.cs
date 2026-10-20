using Fin;
using Fin.Copy;

var apiKey = Environment.GetEnvironmentVariable("FINCOPY_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set FINCOPY_API_KEY before running this example.");
    return;
}

using var fin = new FinClient(new FinClientOptions { ApiKey = apiKey });

var accounts = await fin.Copy.Accounts.ListAsync();
foreach (var account in accounts)
    Console.WriteLine($"Hosted account {account.Fleet?.Account?.Id} · broker login {account.Fleet?.Account?.Login}");

// Read the change feed a page at a time. Save each page's cursor after handling its rows; pass the saved cursor next time to
// carry on where you stopped. The first call without a cursor starts at "now".
await foreach (var page in fin.Copy.Changes.ReadPagesAsync())
{
    Console.WriteLine($"{page.Changes?.Count ?? 0} change(s); cursor to save: {page.Cursor}");
}

// WARNING: this call places a real trade. It is queued for the terminal; the change feed then reports it filled or refused.
// await fin.Copy.Orders.MakeAsync("ACCOUNT_ID", new CmdFxsOrdrMake { SYM = "EURUSD", LOT = 0.01 });
