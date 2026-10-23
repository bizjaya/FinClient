using System.Globalization;
using System.Runtime.CompilerServices;
using static Fin.Copy.FinApiQuery;

namespace Fin.Copy;

/// <summary>FinCopy public API resources.</summary>
public sealed class FinCopyResource
{
    internal FinCopyResource(FinHttpTransport transport)
    {
        Accounts = new AccountsResource(transport);
        Orders = new OrdersResource(transport);
        Fx = new FxResource(transport);
        Changes = new ChangesResource(transport);
        Webhooks = new WebhooksResource(transport);
    }

    /// <summary>Accounts, settings, positions, performance and terminal operations.</summary>
    public AccountsResource Accounts { get; }
    /// <summary>Account order history and trading actions.</summary>
    public OrdersResource Orders { get; }
    /// <summary>Cross-account FXS lists, strategies, signals, alerts, brokers and the live stream.</summary>
    public FxResource Fx { get; }
    /// <summary>The change feed.</summary>
    public ChangesResource Changes { get; }
    /// <summary>Webhook configuration and ping.</summary>
    public WebhooksResource Webhooks { get; }
}

/// <summary>Common page and search filters for cross-account FXS lists.</summary>
public sealed class FinFxsListQuery
{
    /// <summary>Paging and sort values.</summary>
    public FinPageQuery Page { get; set; } = new();
    /// <summary>Optional account number filter.</summary>
    public long? Acno { get; set; }
    /// <summary>Optional pair number filter.</summary>
    public long? Prno { get; set; }
    /// <summary>Optional demo-account filter.</summary>
    public bool? Demo { get; set; }
    /// <summary>Optional platform name.</summary>
    public string? Platform { get; set; }
    /// <summary>Search text.</summary>
    public string? Search { get; set; }
}

/// <summary>One server-sent event from the member change stream.</summary>
public sealed class FinStreamEvent
{
    /// <summary>Creates one parsed server-sent event.</summary>
    public FinStreamEvent(string eventName, string data) { Event = eventName; Data = data; }
    /// <summary>The SSE event name.</summary>
    public string Event { get; }
    /// <summary>The SSE data payload.</summary>
    public string Data { get; }
}

/// <summary>Account listing, lifecycle, settings, symbols and account-level results.</summary>
public sealed class AccountsResource
{
    private readonly FinHttpTransport _http;
    internal AccountsResource(FinHttpTransport http) => _http = http;

    /// <summary>Lists reachable accounts by ACNO, with an optional external reference filter.</summary>
    public Task<FinPage<DtoMemberAccount>> ListAsync(FinPageQuery? page = null, string? externalRef = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoMemberAccount>("GET", Query("v1/fxs/accounts", page, ("externalRef", externalRef)), cancellationToken: cancellationToken);

    /// <summary>Adds a hosted account or reconnects an earlier disconnected account for the signed-in member or API-key owner.
    /// Retrying with the same idempotency key returns the account made or reconnected by the first request.</summary>
    public Task<DtoFleetAccount> MakeAsync(CmdMemberAccountMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFleetAccount>("POST", "v1/fxs/accounts", command, idempotencyKey, cancellationToken);

    /// <summary>Creates a demo account.</summary>
    public Task<DtoFleetAccount> MakeDemoAsync(CmdMemberDemoMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFleetAccount>("POST", "v1/fxs/accounts/demo", command, idempotencyKey, cancellationToken);

    /// <summary>Reads demo terminal availability for one platform.</summary>
    public Task<DtoDemoAvailability> DemoAvailabilityAsync(string? platform = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoDemoAvailability>("GET", Query("v1/fxs/accounts/demo-availability", null, ("platform", platform)), cancellationToken: cancellationToken);

    /// <summary>Reads one account by ACNO.</summary>
    public Task<DtoMemberAccount> GetAsync(long acno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoMemberAccount>("GET", Account(acno), cancellationToken: cancellationToken);

    /// <summary>Sets or clears the account's external reference.</summary>
    public Task<DtoTermAccount> SetExternalRefAsync(long acno, CmdTermAccountExternalRef command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoTermAccount>("PUT", Account(acno) + "/external-ref", command, idempotencyKey, cancellationToken);

    /// <summary>Reads trading settings for one account.</summary>
    public Task<DtoFxsAcct> GetSettingsAsync(long acno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAcct>("GET", Account(acno) + "/settings", cancellationToken: cancellationToken);

    /// <summary>Changes trading settings for one account.</summary>
    public Task<DtoFxsAcct> PatchSettingsAsync(long acno, CmdApiFxsAcctPatch command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAcct>("PATCH", Account(acno) + "/settings", command, idempotencyKey, cancellationToken);

    /// <summary>Lists the account's pairs.</summary>
    public Task<FinPage<DtoFxsPair>> ListPairsAsync(long acno, FinPageQuery? page = null, string? search = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsPair>("GET", Query(Account(acno) + "/pairs", page, ("search", search)), cancellationToken: cancellationToken);

    /// <summary>Reads a pair by PRNO.</summary>
    public Task<DtoFxsPair> GetPairAsync(long acno, long prno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPair>("GET", $"{Account(acno)}/pairs/{Number(prno, nameof(prno))}", cancellationToken: cancellationToken);

    /// <summary>Changes one pair's settings.</summary>
    public Task<DtoFxsPair> PatchPairAsync(long acno, long prno, CmdApiFxsPairPatch command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPair>("PATCH", $"{Account(acno)}/pairs/{Number(prno, nameof(prno))}", command, idempotencyKey, cancellationToken);

    /// <summary>Reads the account's broker symbols and enabled list.</summary>
    public Task<DtoFxsSymbolCatalog> SymbolsAsync(long acno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsSymbolCatalog>("GET", Account(acno) + "/symbols", cancellationToken: cancellationToken);

    /// <summary>Adds or removes enabled symbols.</summary>
    public Task<DtoFxsAccountState> EditSymbolsAsync(long acno, CmdFxsSymbolsEdit command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("PUT", Account(acno) + "/symbols", command, idempotencyKey, cancellationToken);

    /// <summary>Reads one symbol's specification and latest quote.</summary>
    public Task<DtoFxsSymbolSpec> QuoteAsync(long acno, string symbol, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsSymbolSpec>("GET", Account(acno) + "/quotes/" + Uri.EscapeDataString(Required(symbol, nameof(symbol))), cancellationToken: cancellationToken);

    /// <summary>Lists live orders for the account.</summary>
    public Task<FinPage<DtoFxsOrdr>> OrdersAsync(long acno, FinPageQuery? page = null, string? search = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdr>("GET", Query(Account(acno) + "/orders", page, ("search", search)), cancellationToken: cancellationToken);

    /// <summary>Reads one live or settled order.</summary>
    public Task<DtoFxsOrdr> OrderAsync(long acno, long oid, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdr>("GET", $"{Account(acno)}/orders/{Number(oid, nameof(oid))}", cancellationToken: cancellationToken);

    /// <summary>Lists settled orders for the account.</summary>
    public Task<FinPage<DtoFxsOrdh>> HistoryAsync(long acno, FinPageQuery? page = null, string? search = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdh>("GET", Query(Account(acno) + "/history", page, ("search", search)), cancellationToken: cancellationToken);

    /// <summary>Reads one settled order by OID.</summary>
    public Task<DtoFxsOrdh> HistoryItemAsync(long acno, long oid, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdh>("GET", $"{Account(acno)}/history/{Number(oid, nameof(oid))}", cancellationToken: cancellationToken);

    /// <summary>Lists DCA proposals waiting for an answer.</summary>
    public Task<IReadOnlyList<DtoFxsOrdr>> DcaQueueAsync(long acno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoFxsOrdr>>("GET", Account(acno) + "/dca", cancellationToken: cancellationToken);

    /// <summary>Lists daily performance rows.</summary>
    public Task<IReadOnlyList<DtoFxsPerf>> PerformanceAsync(long acno, DateTimeOffset? fromDt = null,
        DateTimeOffset? toDt = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoFxsPerf>>("GET", Query(Account(acno) + "/performance", null,
            ("fromDt", DateValue(fromDt)), ("toDt", DateValue(toDt))), cancellationToken: cancellationToken);

    /// <summary>Reads the account's performance window.</summary>
    public Task<DtoFxsPerfWindow> PerformanceWindowAsync(long acno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPerfWindow>("GET", Account(acno) + "/performance/window", cancellationToken: cancellationToken);

    /// <summary>Reads performance bars for an optional UTC date range.</summary>
    public Task<DtoFxsPerfBars> PerformanceBarsAsync(long acno, DateTimeOffset? fromDt = null,
        DateTimeOffset? toDt = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPerfBars>("GET", Query(Account(acno) + "/performance/bars", null,
            ("fromDt", DateValue(fromDt)), ("toDt", DateValue(toDt))), cancellationToken: cancellationToken);

    /// <summary>Lists paged activity for one account.</summary>
    public Task<FinPage<DtoMemberActivity>> ActivityAsync(long acno, FinPageQuery? page = null, string? search = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoMemberActivity>("GET", Query(Account(acno) + "/activity", page, ("search", search)), cancellationToken: cancellationToken);

    /// <summary>Previews applying a strategy (the account owner's or a FinCopy preset) to the account: what each symbol would
    /// change and what it keeps. Nothing changes. Needs Trade permission.</summary>
    public Task<DtoFxsStgCascadePreview> PreviewStrategyAsync(long acno, CmdApiFxsStgApply command,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsStgCascadePreview>("POST", Account(acno) + "/strategy/preview", command, cancellationToken: cancellationToken);

    /// <summary>Applies a strategy to the account and its symbols; a side holding orders keeps what it must keep. Needs Trade
    /// permission.</summary>
    public Task<DtoFxsStgCascadeApplyResult> ApplyStrategyAsync(long acno, CmdApiFxsStgApply command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsStgCascadeApplyResult>("POST", Account(acno) + "/strategy/apply", command, idempotencyKey, cancellationToken);

    /// <summary>Lists recent terminal operations for one account.</summary>
    public Task<FinPage<DtoApiTermOperation>> OperationsAsync(long acno, FinPageQuery? page = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoApiTermOperation>("GET", Query(Account(acno) + "/operations", page), cancellationToken: cancellationToken);

    /// <summary>Queues a broker-password change.</summary>
    public Task<DtoOperationQueued> ChangePasswordAsync(long acno, CmdTermAccountPassword command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoOperationQueued>("POST", Account(acno) + "/password", command, idempotencyKey, cancellationToken);

    /// <summary>Queues a start operation.</summary>
    public Task<DtoOperationQueued> StartAsync(long acno, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(acno, "start", idempotencyKey, cancellationToken);
    /// <summary>Queues a stop operation.</summary>
    public Task<DtoOperationQueued> StopAsync(long acno, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(acno, "stop", idempotencyKey, cancellationToken);
    /// <summary>Queues a restart operation.</summary>
    public Task<DtoOperationQueued> RestartAsync(long acno, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        OperationAsync(acno, "restart", idempotencyKey, cancellationToken);
    /// <summary>Disconnects the hosted terminal, forgets its broker password and frees its seat. Customer account settings
    /// and trade history remain; a free demo's account and terminal records are deleted. Open orders stay at the broker,
    /// and virtual exits stop working while no terminal runs. Pass tradingPin when a member session requires it. The
    /// Idempotency-Key lets a retry return the same operation.</summary>
    public Task<DtoOperationQueued> RemoveAsync(long acno, string? idempotencyKey = null,
        CancellationToken cancellationToken = default, string? tradingPin = null) =>
        _http.SendAsync<DtoOperationQueued>(HttpMethod.Delete.Method, Account(acno),
            new CmdMemTerminalDisconnect { TrPin = tradingPin }, idempotencyKey, cancellationToken);

    private Task<DtoOperationQueued> OperationAsync(long acno, string suffix, string? key, CancellationToken ct,
        HttpMethod? method = null) => _http.SendAsync<DtoOperationQueued>(method?.Method ?? "POST",
            Account(acno) + (suffix.Length == 0 ? "" : "/" + suffix), idempotencyKey: key, cancellationToken: ct);

    internal static string Account(long acno) => $"v1/fxs/accounts/{Number(acno, nameof(acno), allowZero: false)}";
    internal static string Number(long value, string name, bool allowZero = true)
    {
        if (value < (allowZero ? 0 : 1)) throw new ArgumentOutOfRangeException(name);
        return value.ToString(CultureInfo.InvariantCulture);
    }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A value is required.", name) : value.Trim();
    private static string? DateValue(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>Order history and trade actions.</summary>
public sealed class OrdersResource
{
    private readonly FinHttpTransport _http;
    internal OrdersResource(FinHttpTransport http) => _http = http;

    /// <summary>Preflights a trade request.</summary>
    public Task<DtoFxsPreInfo> PreInfoAsync(long acno, CmdApiFxsPreInfo command, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPreInfo>("POST", AccountsResource.Account(acno) + "/trade/preinfo", command, cancellationToken: cancellationToken);

    /// <summary>Sends one trading action. A successful response means queued, not filled.</summary>
    public Task<DtoFxsAccountState> MakeAsync(long acno, CmdApiFxsOrdrMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(acno) + "/trade", command, idempotencyKey, cancellationToken);

    /// <summary>Sends paired BUY and SELL actions.</summary>
    public Task<DtoFxsAccountState> HedgeAsync(long acno, CmdApiFxsHedgeMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(acno) + "/trade/hedge", command, idempotencyKey, cancellationToken);

    /// <summary>Modifies a trade or pending order.</summary>
    public Task<DtoFxsAccountState> ModifyAsync(long acno, CmdFxsModify command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(acno) + "/trade/modify", command, idempotencyKey, cancellationToken);

    /// <summary>Cancels an order that has not reached the terminal.</summary>
    public Task<DtoFxsAccountState> CancelWaitingAsync(long acno, CmdFxsCancelWaiting command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsAccountState>("POST", AccountsResource.Account(acno) + "/trade/cancel", command, idempotencyKey, cancellationToken);

    /// <summary>Accepts or rejects one DCA proposal.</summary>
    public Task<DtoFxsOrdr> AnswerDcaAsync(long acno, long oid, CmdFxsDcaAnswer command,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsOrdr>("POST", $"{AccountsResource.Account(acno)}/dca/{AccountsResource.Number(oid, nameof(oid))}", command, idempotencyKey, cancellationToken);
}

/// <summary>Cross-account lists, strategies, signals, alerts, broker search and server-sent events.</summary>
public sealed class FxResource
{
    private readonly FinHttpTransport _http;
    internal FxResource(FinHttpTransport http) => _http = http;

    /// <summary>Lists pairs across reachable accounts.</summary>
    public Task<FinPage<DtoFxsPair>> PairsAsync(FinFxsListQuery? query = null, CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsPair>("GET", ListQuery("v1/fxs/pairs", query), cancellationToken: cancellationToken);
    /// <summary>Lists open orders across reachable accounts.</summary>
    public Task<FinPage<DtoFxsOrdr>> OrdersAsync(FinFxsListQuery? query = null, CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdr>("GET", ListQuery("v1/fxs/orders", query), cancellationToken: cancellationToken);
    /// <summary>Lists settled orders across reachable accounts.</summary>
    public Task<FinPage<DtoFxsOrdh>> HistoryAsync(FinFxsListQuery? query = null, CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoFxsOrdh>("GET", ListQuery("v1/fxs/history", query), cancellationToken: cancellationToken);
    /// <summary>Lists activity across reachable accounts.</summary>
    public Task<FinPage<DtoMemberActivity>> ActivityAsync(FinFxsListQuery? query = null, CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoMemberActivity>("GET", ListQuery("v1/fxs/activity", query), cancellationToken: cancellationToken);
    /// <summary>HYBRID DCA proposals waiting for an answer across reachable accounts, newest first.</summary>
    public Task<IReadOnlyList<DtoFxsOrdr>> DcaAsync(long? acno = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoFxsOrdr>>("GET", Query("v1/fxs/dca", null, ("acno", acno)), cancellationToken: cancellationToken);
    /// <summary>The newest terminal operations across reachable accounts.</summary>
    public Task<FinPage<DtoApiTermOperation>> OperationsAsync(FinPageQuery? page = null, long? acno = null,
        CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoApiTermOperation>("GET", Query("v1/fxs/operations", page, ("acno", acno)), cancellationToken: cancellationToken);
    /// <summary>The last 30 days by currency book across reachable accounts, or one account with <paramref name="acno"/>.
    /// <paramref name="demo"/> narrows it to demo (true) or real (false) accounts.</summary>
    public Task<DtoFxsPerfWindow> PerformanceWindowAsync(long? acno = null, bool? demo = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPerfWindow>("GET", Query("v1/fxs/performance/window", null, ("acno", acno), ("demo", demo)),
            cancellationToken: cancellationToken);
    /// <summary>Daily performance bars across reachable accounts for an optional UTC date range.</summary>
    public Task<DtoFxsPerfBars> PerformanceBarsAsync(long? acno = null, bool? demo = null, DateTimeOffset? fromDt = null,
        DateTimeOffset? toDt = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsPerfBars>("GET", Query("v1/fxs/performance/bars", null, ("acno", acno), ("demo", demo),
            ("fromDt", fromDt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("toDt", toDt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))), cancellationToken: cancellationToken);
    /// <summary>Lists the strategies you can apply: your own and FinCopy's presets. With <paramref name="acno"/>, that
    /// account owner's strategies (needs Trade permission on the account).</summary>
    public Task<IReadOnlyList<DtoFxsStg>> StrategiesAsync(long? acno = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoFxsStg>>("GET", Query("v1/fxs/strategies", null, ("acno", acno)), cancellationToken: cancellationToken);
    /// <summary>Reads one strategy.</summary>
    public Task<DtoFxsStg> StrategyAsync(int isno, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsStg>("GET", Strategy(isno), cancellationToken: cancellationToken);
    /// <summary>Creates one of your own strategies. Needs Trade permission.</summary>
    public Task<DtoFxsStg> MakeStrategyAsync(CmdFxsStgMake command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsStg>("POST", "v1/fxs/strategies", command, idempotencyKey, cancellationToken);
    /// <summary>Changes one of your own strategies; fields left unset keep their values. Needs Trade permission.</summary>
    public Task<DtoFxsStg> PatchStrategyAsync(int isno, CmdFxsStgPatch command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoFxsStg>("PATCH", Strategy(isno), command, idempotencyKey, cancellationToken);
    /// <summary>Deletes one of your own strategies. Needs Trade permission.</summary>
    public Task<bool> DeleteStrategyAsync(int isno, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<bool>("DELETE", Strategy(isno), idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    /// <summary>Reads the member's enabled-symbol signal shelf.</summary>
    public Task<DtoShelfSignals> SignalsAsync(CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoShelfSignals>("GET", "v1/fxs/signals", cancellationToken: cancellationToken);
    /// <summary>Lists warning and error activity.</summary>
    public Task<FinPage<DtoMemberActivity>> AlertsAsync(FinPageQuery? page = null, CancellationToken cancellationToken = default) =>
        _http.SendPageAsync<DtoMemberActivity>("GET", Query("v1/fxs/alerts", page), cancellationToken: cancellationToken);
    /// <summary>Opens the live event stream for reachable accounts. Each event names what changed; re-read it. The stream
    /// does not reconnect: it ends when the server closes it (about every 30 minutes) or the connection breaks, so open it
    /// again in a loop, waiting a few seconds between attempts.</summary>
    public IAsyncEnumerable<FinStreamEvent> ReadStreamAsync(CancellationToken cancellationToken = default) =>
        _http.ReadServerSentEventsAsync("v1/fxs/stream", cancellationToken);
    /// <summary>Searches broker servers available when adding an account.</summary>
    public Task<IReadOnlyList<DtoBrokerServerSearch>> BrokersAsync(string? query = null, string? platform = null,
        bool? demosOnly = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<IReadOnlyList<DtoBrokerServerSearch>>("GET", Query("v1/brokers", null,
            ("Q", query), ("Platform", platform), ("DemosOnly", demosOnly)), cancellationToken: cancellationToken);

    private static string Strategy(int isno) => $"v1/fxs/strategies/{AccountsResource.Number(isno, nameof(isno))}";

    private static string ListQuery(string path, FinFxsListQuery? query) => Query(path, query?.Page,
        ("acno", query?.Acno), ("prno", query?.Prno), ("demo", query?.Demo), ("platform", query?.Platform), ("search", query?.Search));
}

/// <summary>Change-feed resource.</summary>
public sealed class ChangesResource
{
    private readonly FinHttpTransport _http;
    internal ChangesResource(FinHttpTransport http) => _http = http;

    /// <summary>Reads one changes page using an optional cursor.</summary>
    public Task<DtoApiChangeFeed> GetAsync(string? cursor = null, int take = 500, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take), "Take must be between 1 and 1000.");
        return _http.SendAsync<DtoApiChangeFeed>("GET", Query("v1/changes", null,
            ("take", take), ("cursor", cursor)), cancellationToken: cancellationToken);
    }

    /// <summary>Reads every available changes page, following each returned cursor.</summary>
    public async IAsyncEnumerable<DtoApiChangeFeed> ReadPagesAsync(string? cursor = null, int take = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(cursor)) seen.Add(cursor!);
        while (true)
        {
            var page = await GetAsync(cursor, take, cancellationToken).ConfigureAwait(false);
            yield return page;
            if (page.More != true) yield break;
            if (string.IsNullOrWhiteSpace(page.Cursor) || !seen.Add(page.Cursor!))
                throw new InvalidOperationException("The FinCopy change feed returned More without a new cursor.");
            cursor = page.Cursor;
        }
    }

    /// <summary>Streams all available changes. Use <see cref="ReadPagesAsync"/> to keep a resume cursor.</summary>
    public async IAsyncEnumerable<DtoApiChange> ReadAllAsync(string? cursor = null, int take = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in ReadPagesAsync(cursor, take, cancellationToken).ConfigureAwait(false))
            foreach (var change in page.Changes ?? []) yield return change;
    }
}

/// <summary>Webhook configuration and test delivery.</summary>
public sealed class WebhooksResource
{
    private readonly FinHttpTransport _http;
    internal WebhooksResource(FinHttpTransport http) => _http = http;

    /// <summary>Reads this key's webhook configuration.</summary>
    public Task<DtoApiWebhook> GetAsync(CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhook>("GET", "v1/webhook", cancellationToken: cancellationToken);
    /// <summary>Sets the webhook destination and returns its signing secret once.</summary>
    public Task<DtoApiWebhook> SetAsync(CmdApiWebhookSet command, string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhook>("PUT", "v1/webhook", command, idempotencyKey, cancellationToken);
    /// <summary>Removes this key's webhook.</summary>
    public Task<bool> DeleteAsync(string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<bool>("DELETE", "v1/webhook", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
    /// <summary>Sends a signed test delivery.</summary>
    public Task<DtoApiWebhookPing> PingAsync(string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.SendAsync<DtoApiWebhookPing>("POST", "v1/webhook/ping", idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
}

internal static class FinApiQuery
{
    internal static string Query(string path, FinPageQuery? page, params (string Name, object? Value)[] filters)
    {
        var values = new List<string>();
        var pageQuery = (page ?? new FinPageQuery()).ToQueryString().TrimStart('?');
        if (pageQuery.Length > 0) values.Add(pageQuery);
        foreach (var (name, value) in filters)
        {
            if (value is null) continue;
            var text = value switch
            {
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            values.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text ?? ""));
        }
        return values.Count == 0 ? path : path + "?" + string.Join("&", values);
    }
}
