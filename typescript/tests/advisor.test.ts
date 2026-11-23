import { expect, it, vi } from "vitest";
import { FinClient } from "../src/index.js";

it("keeps Advisor account routes, PascalCase commands, owner key and idempotency", async () => {
  const calls: { method: string; url: URL; headers: Headers; body: unknown }[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    const url = new URL(String(input));
    calls.push({ method: init?.method ?? "GET", url, headers: new Headers(init?.headers), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    const data = url.pathname.endsWith("/preview") ? { Id: "preview" }
      : url.pathname.endsWith("/history") ? [{ Id: "ask", ContextJson: "sent-context", Refunded: true }]
      : { ACNO: 42, AdvisorBalance: 30, Advisor: { IncludedUsed: true }, Posture: "DEFEND", HourlyRecords: [{ Trigger: "HOURLY" }] };
    return new Response(JSON.stringify({ isOk: true, code: 200, data }), { headers: { "Content-Type": "application/json" } });
  });
  const a = new FinClient({ apiKey: "owner-key", fetch }).copy.accounts;
  const preview = await a.previewAdvisor(42, "PLAN");
  const dashboard = await a.sendAdvisor(42, { ACNO: 42, PreviewId: preview.Id, Kind: "PLAN" }, { idempotencyKey: "send-one" });
  expect(dashboard.AdvisorBalance).toBe(30);
  expect(dashboard.Advisor?.IncludedUsed).toBe(true);
  expect(dashboard.HourlyRecords?.[0]?.Trigger).toBe("HOURLY");
  await a.saveAdvisorSettings(42, { ACNO: 42, Settings: { AdviceTime: "22:00", PaidReasks: false } }, { idempotencyKey: "save-one" });
  expect((await a.advisorHistory(42))[0]?.ContextJson).toBe("sent-context");
  await a.advisorHistoryView(42, "ask/one");
  await a.decideAIBotAdvice(42, { ACNO: 42, AdviceId: "ask", Accept: false }, { idempotencyKey: "decline-one" });
  await a.undoAIBotAdvice(42, { ACNO: 42 }, { idempotencyKey: "undo-one" });
  expect(calls.map(x => x.method)).toEqual(["GET", "POST", "PUT", "GET", "GET", "POST", "POST"]);
  expect(calls[0]?.url.searchParams.get("kind")).toBe("PLAN");
  expect(calls[1]?.body).toEqual({ ACNO: 42, PreviewId: "preview", Kind: "PLAN" });
  expect(calls[4]?.url.pathname).toBe("/v1/fxs/accounts/42/aibot/advisor/history/ask%2Fone");
  expect(calls.filter(x => x.headers.has("Idempotency-Key")).map(x => x.headers.get("Idempotency-Key")))
    .toEqual(["send-one", "save-one", "decline-one", "undo-one"]);
  expect(calls.every(x => x.headers.get("X-Api-Key") === "owner-key")).toBe(true);
});
