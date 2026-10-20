using System.Runtime.CompilerServices;

namespace Fin.Copy;

/// <summary>FinCopy API resources.</summary>
public sealed class FinCopyResource
{
    internal FinCopyResource(FinHttpTransport transport)
    {
        Accounts = new AccountsResource(transport);
        Orders = new OrdersResource(transport);
        Changes = new ChangesResource(transport);
        Webhooks = new WebhooksResource(transport);
    }

    /// <summary>Account listing, details, settings, symbols and terminal actions.</summary>
    public AccountsResource Accounts { get; }
    /// <summary>Orders and trade actions.</summary>
    public OrdersResource Orders { get; }
    /// <summary>The change feed.</summary>
    public ChangesResource Changes { get; }
    /// <summary>Webhook configuration and ping.</summary>
    public WebhooksResource Webhooks { get; }
}


/// <summary>Account listing, details, symbols and terminal actions.</summary>
public sealed class AccountsResource
{
    private readonly FinHttpTransport _http;
    internal AccountsResource(FinHttpTransport http) => _http = http;

    /// <summary>Lists the accounts this API key can reach.</summary>
    public Task<IReadOnlyList<DtoMemberAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoMemberAccount>>("GET", "v1/accounts", cancellationToken: cancellationToken);

    /// <summary>Adds a hosted account for the key's owner.</summary>
    public Task<DtoFleetAccount> MakeAsync(CmdMemberAccountMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFleetAccount>("POST", "v1/accounts/make", command, idempotencyKey, cancellationToken);

    /// <summary>Reads one account and its latest state.</summary>
    public Task<DtoMemberAccount> GetAsync(string accountId, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoMemberAccount>("GET", Account(accountId), cancellationToken: cancellationToken);

    /// <summary>Sets or clears the account's external reference.</summary>
    public Task<DtoTermAccount> SetExternalRefAsync(string accountId, CmdTermAccountExternalRef command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoTermAccount>("PUT", Account(accountId) + "/external-ref", command, idempotencyKey, cancellationToken);

    /// <summary>Reads the account's trading settings.</summary>
    public Task<DtoFxsAcct> GetTradingAsync(string accountId, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAcct>("GET", Account(accountId) + "/fxs", cancellationToken: cancellationToken);

    /// <summary>Changes the account's trading settings.</summary>
    public Task<DtoFxsAcct> PatchTradingAsync(string accountId, CmdFxsAcctPatch command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAcct>("PATCH", Account(accountId) + "/fxs", command, idempotencyKey, cancellationToken);

    /// <summary>Lists the account's symbols and their BUY and SELL books.</summary>
    public Task<FinPage<DtoFxsPair>> ListPairsAsync(string accountId, FinPageQuery? query = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsPair>("GET", Account(accountId) + "/pairs" + (query ?? new FinPageQuery()).ToQueryString(), cancellationToken: cancellationToken);

    /// <summary>Reads one symbol and its settings.</summary>
    public Task<DtoFxsPair> GetPairAsync(string accountId, long prno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPair>("GET", Account(accountId) + "/pairs/" + prno.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken: cancellationToken);

    /// <summary>Changes one symbol's settings.</summary>
    public Task<DtoFxsPair> PatchPairAsync(string accountId, long prno, CmdFxsPairPatch command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPair>("PATCH", Account(accountId) + "/pairs/" + prno.ToString(System.Globalization.CultureInfo.InvariantCulture), command, idempotencyKey, cancellationToken);

    /// <summary>Lists the account's HYBRID DCA proposals waiting for an answer.</summary>
    public Task<IReadOnlyList<DtoFxsOrdr>> DcaQueueAsync(string accountId, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoFxsOrdr>>("GET", Account(accountId) + "/dca-queue", cancellationToken: cancellationToken);

    /// <summary>Reads broker symbols and the account's enabled symbols.</summary>
    public Task<DtoFxsSymbolCatalog> SymbolsAsync(string accountId, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsSymbolCatalog>("GET", Account(accountId) + "/symbols", cancellationToken: cancellationToken);

    /// <summary>Adds and removes symbols from the account's enabled list.</summary>
    public Task<DtoFxsAccountState> EditSymbolsAsync(string accountId, CmdFxsSymbolsEdit command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("PUT", Account(accountId) + "/symbols", command, idempotencyKey, cancellationToken);

    /// <summary>Reads a symbol's broker specification and latest quote.</summary>
    public Task<DtoFxsSymbolSpec> QuoteAsync(string accountId, string symbol, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsSymbolSpec>("GET", Account(accountId) + "/quotes/" + Uri.EscapeDataString(symbol), cancellationToken: cancellationToken);

    /// <summary>Reads daily performance rows for one account and optional UTC date range.</summary>
    public Task<IReadOnlyList<DtoFxsPerf>> PerformanceAsync(string accountId, DateTimeOffset? fromDt = null,
        DateTimeOffset? toDt = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (fromDt.HasValue) query.Add("fromDt=" + Uri.EscapeDataString(fromDt.Value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        if (toDt.HasValue) query.Add("toDt=" + Uri.EscapeDataString(toDt.Value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        return _http.SendAsync<IReadOnlyList<DtoFxsPerf>>("GET", Account(accountId) + "/performance" + (query.Count == 0 ? "" : "?" + string.Join("&", query)), cancellationToken: cancellationToken);
    }

    /// <summary>Reads recent activity for one account.</summary>
    public Task<IReadOnlyList<DtoMemberActivity>> ActivityAsync(string accountId, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoMemberActivity>>("GET", Account(accountId) + "/activity", cancellationToken: cancellationToken);

    /// <summary>Queues a start operation for the hosted terminal.</summary>
    public Task<DtoOperationQueued> StartAsync(string accountId, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(accountId, "start", idempotencyKey, cancellationToken);
    /// <summary>Queues a stop operation for the hosted terminal.</summary>
    public Task<DtoOperationQueued> StopAsync(string accountId, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(accountId, "stop", idempotencyKey, cancellationToken);
    /// <summary>Queues a restart operation for the hosted terminal.</summary>
    public Task<DtoOperationQueued> RestartAsync(string accountId, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(accountId, "restart", idempotencyKey, cancellationToken);
    /// <summary>Queues removal of the hosted account and its terminal.</summary>
    public Task<DtoOperationQueued> RemoveAsync(string accountId, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(accountId, "remove", idempotencyKey, cancellationToken);

    private Task<DtoOperationQueued> OperationAsync(string accountId, string operation, string? key, CancellationToken ct) =>
        _http.SendAsync<DtoOperationQueued>("POST", Account(accountId) + "/" + operation, idempotencyKey: key, cancellationToken: ct);

    internal static string Account(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Account id is required.", nameof(accountId));
        return "v1/accounts/" + Uri.EscapeDataString(accountId);
    }
}

/// <summary>Orders and trade actions.</summary>
public sealed class OrdersResource
{
    private readonly FinHttpTransport _http;
    internal OrdersResource(FinHttpTransport http) => _http = http;

    /// <summary>Lists live orders, including queued and pending orders.</summary>
    public Task<FinPage<DtoFxsOrdr>> ListAsync(string accountId, FinPageQuery? query = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdr>("GET", AccountsResource.Account(accountId) + "/orders" + (query ?? new FinPageQuery()).ToQueryString(), cancellationToken: cancellationToken);

    /// <summary>Lists settled orders, newest first.</summary>
    public Task<FinPage<DtoFxsOrdh>> HistoryAsync(string accountId, FinPageQuery? query = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdh>("GET", AccountsResource.Account(accountId) + "/orders/history" + (query ?? new FinPageQuery()).ToQueryString(), cancellationToken: cancellationToken);

    /// <summary>Reads one settled order.</summary>
    public Task<DtoFxsOrdh> HistoryItemAsync(string accountId, long oid, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdh>("GET", AccountsResource.Account(accountId) + "/orders/history/" + Id(oid), cancellationToken: cancellationToken);

    /// <summary>Reads one live or settled order.</summary>
    public Task<DtoFxsOrdr> GetAsync(string accountId, long oid, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdr>("GET", AccountsResource.Account(accountId) + "/orders/" + Id(oid), cancellationToken: cancellationToken);

    /// <summary>Sends one FXS action. A successful response means queued for the terminal, not filled.</summary>
    public Task<DtoFxsAccountState> MakeAsync(string accountId, CmdFxsOrdrMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(accountId) + "/orders", command, idempotencyKey, cancellationToken);

    /// <summary>Accepts or rejects one HYBRID DCA proposal.</summary>
    public Task<DtoFxsOrdr> AnswerDcaAsync(string accountId, long oid, CmdFxsDcaAnswer command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdr>("POST", AccountsResource.Account(accountId) + "/dca/" + Id(oid) + "/answer", command, idempotencyKey, cancellationToken);

    /// <summary>Reads the quote, limits, books and settings used by the trade ticket.</summary>
    public Task<DtoFxsPreInfo> PreInfoAsync(string accountId, CmdFxsPreInfo command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPreInfo>("POST", AccountsResource.Account(accountId) + "/orders/preinfo", command, idempotencyKey, cancellationToken);

    /// <summary>Sends a BUY and SELL trade together. A successful response means queued, not filled.</summary>
    public Task<DtoFxsAccountState> HedgeAsync(string accountId, CmdFxsHedgeMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(accountId) + "/orders/hedge", command, idempotencyKey, cancellationToken);

    /// <summary>Changes an order's TP field, SL field, expiry or pending price.</summary>
    public Task<DtoFxsAccountState> ModifyAsync(string accountId, CmdFxsModify command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(accountId) + "/trades/modify", command, idempotencyKey, cancellationToken);

    /// <summary>Cancels an order that has not yet been delivered to the terminal.</summary>
    public Task<DtoFxsAccountState> CancelWaitingAsync(string accountId, CmdFxsCancelWaiting command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(accountId) + "/trades/cancel-waiting", command, idempotencyKey, cancellationToken);

    private static string Id(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Changes feed resources.</summary>
public sealed class ChangesResource
{
    private readonly FinHttpTransport _http;
    internal ChangesResource(FinHttpTransport http) => _http = http;

    /// <summary>Reads one changes page using an optional cursor.</summary>
    public Task<DtoApiChangeFeed> GetAsync(string? cursor = null, int take = 500, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take), "Take must be between 1 and 1000.");
        var query = "?take=" + take.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(cursor)) query += "&cursor=" + Uri.EscapeDataString(cursor);
        return _http.SendAsync<DtoApiChangeFeed>("GET", "v1/accounts/changes" + query, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads the change feed page by page, starting at <paramref name="cursor"/> (or at "now" when it is null) and following
    /// each returned cursor while more rows remain. Process a page's rows, then save its <c>Cursor</c>: after a restart, pass
    /// the saved cursor here to carry on where you stopped.
    /// </summary>
    public async IAsyncEnumerable<DtoApiChangeFeed> ReadPagesAsync(string? cursor = null, int take = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(cursor)) seen.Add(cursor!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await GetAsync(cursor, take, cancellationToken).ConfigureAwait(false);
            yield return page;
            if (page.More != true) yield break;
            var nextCursor = page.Cursor;
            if (string.IsNullOrWhiteSpace(nextCursor) || !seen.Add(nextCursor!))
                throw new InvalidOperationException("The FinCopy change feed returned More without a new cursor.");
            cursor = nextCursor;
        }
    }

    /// <summary>
    /// Streams every available change, following each returned cursor while more rows remain. This stream does not expose the
    /// cursor; use <see cref="ReadPagesAsync"/> when the cursor must be saved to resume later.
    /// </summary>
    public async IAsyncEnumerable<DtoApiChange> ReadAllAsync(string? cursor = null, int take = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in ReadPagesAsync(cursor, take, cancellationToken).ConfigureAwait(false))
        {
            if (page.Changes is null) continue;
            foreach (var change in page.Changes) yield return change;
        }
    }
}

/// <summary>Webhook management resources.</summary>
public sealed class WebhooksResource
{
    private readonly FinHttpTransport _http;
    internal WebhooksResource(FinHttpTransport http) => _http = http;

    /// <summary>Reads this key's webhook configuration.</summary>
    public Task<DtoApiWebhook> GetAsync(CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhook>("GET", "v1/accounts/webhook", cancellationToken: cancellationToken);

    /// <summary>Sets the HTTPS destination and returns its signing secret once.</summary>
    public Task<DtoApiWebhook> SetAsync(CmdApiWebhookSet command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhook>("PUT", "v1/accounts/webhook", command, idempotencyKey, cancellationToken);

    /// <summary>Removes this key's webhook.</summary>
    public Task<bool> DeleteAsync(string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<bool>("DELETE", "v1/accounts/webhook", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);

    /// <summary>Sends a signed test POST to this key's webhook.</summary>
    public Task<DtoApiWebhookPing> PingAsync(string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhookPing>("POST", "v1/accounts/webhook/ping", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
}
