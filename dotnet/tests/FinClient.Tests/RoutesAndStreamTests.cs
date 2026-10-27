using System.Net;
using System.Text;
using Fin;
using Fin.Copy;
using Xunit;

namespace Fin.Tests;

public sealed class RoutesAndStreamTests
{
    private const string Ok = "{\"data\":{},\"code\":200,\"isOk\":true}";

    [Fact]
    public void SseParserSkipsCommentsJoinsDataAndNamesUnnamedEvents()
    {
        var parser = new FinSseParser();
        var events = new List<FinStreamEvent>();
        foreach (var line in new[] { ": open", "retry: 5000", "event: orders", "data: {}", "", "data: one", "data:two", "", "event: empty", "" })
            if (parser.Add(line) is { } item) events.Add(item);

        Assert.Equal(2, events.Count);
        Assert.Equal("orders", events[0].Event);
        Assert.Equal("{}", events[0].Data);
        Assert.Equal("message", events[1].Event);
        Assert.Equal("one\ntwo", events[1].Data);
    }

    [Fact]
    public void SseParserDeliversAnEventLeftOpenWhenTheStreamEnds()
    {
        var parser = new FinSseParser();
        Assert.Null(parser.Add("event: pairs"));
        Assert.Null(parser.Add("data: {}"));
        var last = parser.Complete();
        Assert.Equal("pairs", last!.Event);
        Assert.Null(new FinSseParser().Complete());
    }

    [Fact]
    public async Task StreamYieldsEventsAndSendsTheKey()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(": open\n\nevent: orders\ndata: {}\n\nevent: pairs\ndata: {}\n\n", Encoding.UTF8, "text/event-stream")
        });
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        var names = new List<string>();
        await foreach (var item in fin.Copy.Fx.ReadStreamAsync()) names.Add(item.Event);

        Assert.Equal(new[] { "orders", "pairs" }, names);
        Assert.Equal("https://api.fincopy.io/v1/fxs/stream", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("test", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task BrokerSearchUsesTheQueryParameterExpectedByTheApi()
    {
        var handler = new Handler(_ => Json("{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        await fin.Copy.Fx.BrokersAsync("MetaTrader 5", "MT5", true);

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("query=MetaTrader%205", query, StringComparison.Ordinal);
        Assert.DoesNotContain("Q=", query, StringComparison.Ordinal);
        Assert.Contains("Platform=MT5", query, StringComparison.Ordinal);
        Assert.Contains("DemosOnly=true", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamThatCannotOpenIsANetworkError()
    {
        var handler = new Handler(_ => throw new HttpRequestException("refused"));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        var error = await Assert.ThrowsAsync<FinApiException>(async () =>
        {
            await foreach (var _ in fin.Copy.Fx.ReadStreamAsync()) { }
        });

        Assert.Equal("network", error.ErrorCode);
        Assert.Equal(0, error.StatusCode);
    }

    [Fact]
    public async Task StreamBrokenMidwayIsANetworkError()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BreakingStream(Encoding.UTF8.GetBytes("event: orders\ndata: {}\n\n")))
        });
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        var names = new List<string>();
        var error = await Assert.ThrowsAsync<FinApiException>(async () =>
        {
            await foreach (var item in fin.Copy.Fx.ReadStreamAsync()) names.Add(item.Event);
        });

        Assert.Equal(new[] { "orders" }, names);
        Assert.Equal("network", error.ErrorCode);
    }

    [Fact]
    public async Task CrossAccountListsCarryTheirFilters()
    {
        var handler = new Handler(_ => Json("{\"data\":[],\"code\":200,\"isOk\":true}"));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        await fin.Copy.Fx.PairsAsync(new FinFxsListQuery { Acno = 1234567890, Demo = true, Page = new FinPageQuery { PageNo = 2 } });
        await fin.Copy.Fx.OperationsAsync(new FinPageQuery { PageSize = 5 }, 1234567890);
        await fin.Copy.Fx.DcaAsync(1234567890);

        Assert.Equal("https://api.fincopy.io/v1/fxs/pairs?PageNo=2&acno=1234567890&demo=true", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("https://api.fincopy.io/v1/fxs/operations?PageSize=5&acno=1234567890", handler.Requests[1].RequestUri!.AbsoluteUri);
        Assert.Equal("https://api.fincopy.io/v1/fxs/dca?acno=1234567890", handler.Requests[2].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CrossAccountPerformanceReadsEveryAccountUnlessNarrowed()
    {
        var handler = new Handler(_ => Json(Ok));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        await fin.Copy.Fx.PerformanceWindowAsync();
        await fin.Copy.Fx.PerformanceBarsAsync(acno: 1234567890, demo: false);

        Assert.Equal("https://api.fincopy.io/v1/fxs/performance/window", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("https://api.fincopy.io/v1/fxs/performance/bars?acno=1234567890&demo=false", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task TradeActionsUseTheirRoutesAndAnIdempotencyKey()
    {
        var handler = new Handler(_ => Json(Ok));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        await fin.Copy.Orders.HedgeAsync(1234567890, new CmdApiFxsHedgeMake { SYM = "EURUSD" });
        await fin.Copy.Orders.ModifyAsync(1234567890, new CmdFxsModify { Ticket = 5 });
        await fin.Copy.Orders.CancelWaitingAsync(1234567890, new CmdFxsCancelWaiting { Oid = 6 });
        await fin.Copy.Orders.AnswerDcaAsync(1234567890, 7, new CmdFxsDcaAnswer { Accept = true });

        var account = "https://api.fincopy.io/v1/fxs/accounts/1234567890";
        Assert.Equal(new[] { $"{account}/trade/hedge", $"{account}/trade/modify", $"{account}/trade/cancel", $"{account}/dca/7" },
            handler.Requests.Select(x => x.RequestUri!.AbsoluteUri));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.False(string.IsNullOrWhiteSpace(request.Headers.GetValues("Idempotency-Key").Single()));
        });
        Assert.Contains("\"Accept\":true", handler.Bodies[3]);
    }

    [Fact]
    public async Task RemoveSendsADeleteWithItsKey()
    {
        var handler = new Handler(_ => Json("{\"data\":{\"OperationId\":\"op-1\"},\"code\":200,\"isOk\":true}"));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        var queued = await fin.Copy.Accounts.RemoveAsync(1234567890, "remove-once");

        Assert.Equal("op-1", queued.OperationId);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Equal("https://api.fincopy.io/v1/fxs/accounts/1234567890", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("remove-once", handler.Requests[0].Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task StrategyRoutesPreviewApplyAndEdit()
    {
        var handler = new Handler(request => Json(request.Method == HttpMethod.Get ? "{\"data\":[],\"code\":200,\"isOk\":true}" : Ok));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        await fin.Copy.Accounts.PreviewStrategyAsync(1234567890, new CmdApiFxsStgApply { ISNO = 42 });
        await fin.Copy.Accounts.ApplyStrategyAsync(1234567890, new CmdApiFxsStgApply { ISNO = 42 });
        await fin.Copy.Fx.MakeStrategyAsync(new CmdFxsStgMake { StratName = "Mine" });
        await fin.Copy.Fx.PatchStrategyAsync(42, new CmdFxsStgPatch { StratName = "Renamed" });
        await fin.Copy.Fx.StrategiesAsync(1234567890);

        var uris = handler.Requests.Select(x => $"{x.Method} {x.RequestUri!.AbsoluteUri}").ToArray();
        Assert.Equal(new[]
        {
            "POST https://api.fincopy.io/v1/fxs/accounts/1234567890/strategy/preview",
            "POST https://api.fincopy.io/v1/fxs/accounts/1234567890/strategy/apply",
            "POST https://api.fincopy.io/v1/fxs/strategies",
            "PATCH https://api.fincopy.io/v1/fxs/strategies/42",
            "GET https://api.fincopy.io/v1/fxs/strategies?acno=1234567890"
        }, uris);
        Assert.Contains("\"ISNO\":42", handler.Bodies[1]);
    }

    [Fact]
    public async Task DeleteStrategyReadsTheBooleanReply()
    {
        var handler = new Handler(_ => Json("{\"data\":true,\"code\":200,\"isOk\":true}"));
        using var fin = new FinClient(new FinClientOptions("test"), handler);

        Assert.True(await fin.Copy.Fx.DeleteStrategyAsync(42));
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    /// <summary>Returns its bytes, then fails the next read as a dropped connection would.</summary>
    private sealed class BreakingStream(byte[] first) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= first.Length) throw new IOException("The connection was reset.");
            var length = Math.Min(count, first.Length - _position);
            Array.Copy(first, _position, buffer, offset, length);
            _position += length;
            return length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= first.Length) throw new IOException("The connection was reset.");
            var length = Math.Min(buffer.Length, first.Length - _position);
            first.AsMemory(_position, length).CopyTo(buffer);
            _position += length;
            return new ValueTask<int>(length);
        }
    }
}
