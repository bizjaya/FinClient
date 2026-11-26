using System.Net;
using System.Text;
using System.Text.Json;
using Fin.Copy;
using Xunit;

namespace Fin.Tests;

public sealed class AdvisorTests
{
    [Fact]
    public async Task AdvisorRoutesKeepAccountIdentityCommandsAndIdempotency()
    {
        var handler = new Handler();
        using var fin = new FinClient(new FinClientOptions("owner-key"), handler);
        var accounts = fin.Copy.Accounts;
        var preview = await accounts.PreviewAdvisorAsync(42, "PLAN", ["EURUSD", "XAUUSD"]);
        Assert.Equal("preview", preview.PreviewId);
        var ask = await accounts.SendAdvisorAsync(42, new CmdAdvAskMake { ACNO = 42, PreviewId = preview.PreviewId, Kind = "PLAN" }, "send-one");
        Assert.Equal(AdvAskStatus.QUEUED, ask.Status);
        var state = await accounts.GetAdvisorSettingsAsync(42);
        Assert.Equal(30, state.Balance);
        await accounts.SaveAdvisorSettingsAsync(42, new CmdAdvStgEdit { ACNO = 42,
            Settings = new DtoAdvStg { Enabled = true, AdviceTime = "22:00", PaidReasks = false, EveryFourHours = false } }, "save-one");
        var history = await accounts.AdvisorHistoryAsync(42, new FinPageQuery { PageNo = 2, PageSize = 10 });
        Assert.True(history.Items.Single().Refunded);
        Assert.Equal(2, history.Paging?.PageNo);
        var detail = await accounts.AdvisorHistoryViewAsync(42, "ask/one", sent: true);
        Assert.Equal("ask", detail.Id);
        await accounts.DecideAdvisorAsync(42, "ask/one", new CmdAdvAskPtch { ACNO = 42, Account = AdvDecision.DECLINE,
            Pairs = [new DtoAdvPairDecision { SYM = "EURUSD", Decision = AdvDecision.ACCEPT }] }, "decide-one");
        await accounts.UndoAdvisorAsync(42, new CmdAdvAskRset { ACNO = 42 }, "undo-one");

        Assert.Equal(new[] { "GET", "POST", "GET", "PUT", "GET", "GET", "PATCH", "POST" }, handler.Calls.Select(x => x.Method));
        Assert.Equal(new[]
        {
            "/v1/fxs/accounts/42/advisor/preview", "/v1/fxs/accounts/42/advisor/asks", "/v1/fxs/accounts/42/advisor/settings",
            "/v1/fxs/accounts/42/advisor/settings", "/v1/fxs/accounts/42/advisor/asks", "/v1/fxs/accounts/42/advisor/asks/ask%2Fone",
            "/v1/fxs/accounts/42/advisor/asks/ask%2Fone", "/v1/fxs/accounts/42/advisor/undo",
        }, handler.Calls.Select(x => x.Url.AbsolutePath));
        Assert.Equal("?kind=PLAN&syms=EURUSD&syms=XAUUSD", handler.Calls[0].Url.Query);
        Assert.Equal("?PageNo=2&PageSize=10", handler.Calls[4].Url.Query);
        Assert.Equal("?sent=true", handler.Calls[5].Url.Query);
        Assert.Equal(new[] { "send-one", "save-one", "decide-one", "undo-one" }, handler.Calls.Where(x => x.Key is not null).Select(x => x.Key));
        Assert.All(handler.Calls, x => Assert.Equal("owner-key", x.OwnerKey));
        using var sent = JsonDocument.Parse(handler.Calls[1].Body);
        Assert.Equal(42, sent.RootElement.GetProperty("ACNO").GetInt64());
        Assert.Equal("preview", sent.RootElement.GetProperty("PreviewId").GetString());
        using var saved = JsonDocument.Parse(handler.Calls[3].Body);
        Assert.False(saved.RootElement.GetProperty("Settings").GetProperty("PaidReasks").GetBoolean());
        using var decided = JsonDocument.Parse(handler.Calls[6].Body);
        Assert.Equal("DECLINE", decided.RootElement.GetProperty("Account").GetString());
        Assert.Equal("ACCEPT", decided.RootElement.GetProperty("Pairs")[0].GetProperty("Decision").GetString());
    }

    [Fact]
    public async Task PreviewWithoutKindOrPairsSendsNoQuery()
    {
        var handler = new Handler();
        using var fin = new FinClient(new FinClientOptions("owner-key"), handler);

        await fin.Copy.Accounts.PreviewAdvisorAsync(42);
        await fin.Copy.Accounts.AdvisorHistoryViewAsync(42, "ask");

        Assert.Equal("", handler.Calls[0].Url.Query);
        Assert.Equal("", handler.Calls[1].Url.Query);
    }

    [Fact]
    public void AdvisorStateDeserializesSettingsCreditsAndTheNewestAnswer()
    {
        var state = JsonSerializer.Deserialize<DtoAdvState>("""
            {"ACNO":42,"Allowed":true,"Balance":30,"AsksToday":2,"IncludedUsed":true,"UndoAskId":"ask",
             "Settings":{"Enabled":true,"AdviceTime":"22:00","AdviceMode":"ASK","ExcludedSymbols":["EURUSD"],"NotifyEmail":true},
             "Newest":{"Id":"ask","Status":"READY","CanDecide":true,"Credits":10,"Refunded":false,
                       "Pairs":[{"SYM":"EURUSD","Stance":"BUY_ONLY","Decision":"NONE"}]}}
            """, FinHttpTransport.ReadOptions)!;
        Assert.Equal(30, state.Balance);
        Assert.True(state.IncludedUsed);
        Assert.Equal("EURUSD", state.Settings!.ExcludedSymbols!.Single());
        Assert.True(state.Settings.NotifyEmail);
        Assert.Equal(AdvAskStatus.READY, state.Newest!.Status);
        Assert.Equal("BUY_ONLY", state.Newest.Pairs!.Single().Stance);
        Assert.Equal(AdvDecision.NONE, state.Newest.Pairs!.Single().Decision);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(string Method, Uri Url, string Body, string? Key, string OwnerKey)> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!, body,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Headers.GetValues("X-Api-Key").Single()));
            var path = request.RequestUri!.AbsolutePath;
            var reply = path.EndsWith("/preview") ? "{\"isOk\":true,\"code\":200,\"data\":{\"PreviewId\":\"preview\",\"Chars\":1200}}"
                : path.EndsWith("/settings") ? "{\"isOk\":true,\"code\":200,\"data\":{\"ACNO\":42,\"Balance\":30}}"
                : path.EndsWith("/asks") && request.Method == HttpMethod.Get
                    ? "{\"isOk\":true,\"code\":200,\"data\":[{\"Id\":\"ask\",\"Status\":\"APPLIED\",\"Refunded\":true}],"
                      + "\"paging\":{\"pageNo\":2,\"pageSize\":10,\"pageCount\":2,\"resCount\":11}}"
                : "{\"isOk\":true,\"code\":200,\"data\":{\"Id\":\"ask\",\"Status\":\"QUEUED\"}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
