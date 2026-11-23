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
        var preview = await accounts.PreviewAdvisorAsync(42, "PLAN");
        Assert.Equal("preview", preview.Id);
        await accounts.SendAdvisorAsync(42, new CmdAdvisorSend { ACNO = 42, PreviewId = preview.Id, Kind = "PLAN" }, "send-one");
        await accounts.SaveAdvisorSettingsAsync(42, new CmdAdvisorSettings { ACNO = 42,
            Settings = new FxsAdvisorSettings { AdviceTime = "22:00", PaidReasks = false, EveryFourHours = false } }, "save-one");
        var history = await accounts.AdvisorHistoryAsync(42);
        Assert.Equal("sent-context", history.Single().ContextJson);
        Assert.True(history.Single().Refunded);
        var detail = await accounts.AdvisorHistoryViewAsync(42, "ask/one");
        Assert.Equal("ask", detail.Id);
        await accounts.DecideAIBotAdviceAsync(42, new CmdAIBotAdviceDecide { ACNO = 42, AdviceId = "ask", Accept = false }, "decline-one");
        await accounts.UndoAIBotAdviceAsync(42, new CmdAIBotAdviceUndo { ACNO = 42 }, "undo-one");
        Assert.Equal(new[] { "GET", "POST", "PUT", "GET", "GET", "POST", "POST" }, handler.Calls.Select(x => x.Method));
        Assert.Equal("?kind=PLAN", handler.Calls[0].Url.Query);
        Assert.EndsWith("/42/aibot/advisor/history/ask%2Fone", handler.Calls[4].Url.AbsoluteUri);
        Assert.Equal(new[] { "send-one", "save-one", "decline-one", "undo-one" }, handler.Calls.Where(x => x.Key is not null).Select(x => x.Key));
        Assert.All(handler.Calls, x => Assert.Equal("owner-key", x.OwnerKey));
        using var sent = JsonDocument.Parse(handler.Calls[1].Body);
        Assert.Equal(42, sent.RootElement.GetProperty("ACNO").GetInt64());
        Assert.Equal("preview", sent.RootElement.GetProperty("PreviewId").GetString());
        using var saved = JsonDocument.Parse(handler.Calls[2].Body);
        Assert.False(saved.RootElement.GetProperty("Settings").GetProperty("PaidReasks").GetBoolean());
    }

    [Fact]
    public void DashboardDeserializesFinCopyAdvisorWalletAndHourlyFacts()
    {
        var dashboard = JsonSerializer.Deserialize<DtoAIBotFullAuto>("""
            {"ACNO":42,"AdvisorBalance":30,"AdvisorAllowed":true,"Advisor":{"Enabled":true,"AdviceTime":"22:00","AdviceMode":"ASK","IncludedUsed":true,"AsksToday":2,"ExcludedSymbols":["EURUSD"]},"Posture":"DEFEND","PostureWhy":"margin","HourlyRecords":[{"Id":"hour","Trigger":"HOURLY"}],"Advice":[{"Id":"ask","Included":false,"Credits":10,"Refunded":true}]}
            """, FinHttpTransport.ReadOptions)!;
        Assert.Equal(30, dashboard.AdvisorBalance);
        Assert.True(dashboard.Advisor!.IncludedUsed);
        Assert.Equal("EURUSD", dashboard.Advisor.ExcludedSymbols!.Single());
        Assert.Equal("DEFEND", dashboard.Posture);
        Assert.Equal("HOURLY", dashboard.HourlyRecords!.Single().Trigger);
        Assert.Equal(10, dashboard.Advice!.Single().Credits);
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
            var data = path.EndsWith("/preview") ? "{\"Id\":\"preview\"}"
                : path.EndsWith("/history") ? "[{\"Id\":\"ask\",\"ContextJson\":\"sent-context\",\"Refunded\":true}]"
                : "{\"Id\":\"ask\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"isOk\":true,\"code\":200,\"data\":" + data + "}", Encoding.UTF8, "application/json") };
        }
    }
}
