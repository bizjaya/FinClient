import { expect, it, vi } from "vitest";
import { FinClient } from "../src/index.js";

type Call = { method: string; url: URL; headers: Headers; body: unknown };

function advisorClient() {
  const calls: Call[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    const url = new URL(String(input));
    const method = init?.method ?? "GET";
    calls.push({ method, url, headers: new Headers(init?.headers), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    const reply = url.pathname.endsWith("/preview") ? { isOk: true, code: 200, data: { PreviewId: "preview", Chars: 1200 } }
      : url.pathname.endsWith("/settings") ? { isOk: true, code: 200, data: { ACNO: 42, Balance: 30, Settings: { NotifyEmail: true } } }
      : url.pathname.endsWith("/asks") && method === "GET"
        ? { isOk: true, code: 200, data: [{ Id: "ask", Status: "APPLIED", Refunded: true }], paging: { pageNo: 2, pageSize: 10, pageCount: 2, resCount: 11 } }
        : { isOk: true, code: 200, data: { Id: "ask", Status: "QUEUED" } };
    return new Response(JSON.stringify(reply), { headers: { "Content-Type": "application/json" } });
  });
  return { calls, accounts: new FinClient({ apiKey: "owner-key", fetch }).copy.accounts };
}

it("keeps Advisor account routes, PascalCase commands, owner key and idempotency", async () => {
  const { calls, accounts: a } = advisorClient();
  const preview = await a.previewAdvisor(42, "PLAN", ["EURUSD", "XAUUSD"]);
  const ask = await a.sendAdvisor(42, { ACNO: 42, PreviewId: preview.PreviewId, Kind: "PLAN" }, { idempotencyKey: "send-one" });
  expect(ask.Status).toBe("QUEUED");
  const state = await a.getAdvisorSettings(42);
  expect(state.Balance).toBe(30);
  expect(state.Settings?.NotifyEmail).toBe(true);
  await a.saveAdvisorSettings(42, { ACNO: 42, Settings: { Enabled: true, AdviceTime: "22:00", PaidReasks: false } }, { idempotencyKey: "save-one" });
  const history = await a.advisorHistory(42, { PageNo: 2, PageSize: 10 });
  expect(history.items[0]?.Refunded).toBe(true);
  expect(history.paging?.pageNo).toBe(2);
  await a.advisorHistoryView(42, "ask/one", true);
  await a.decideAdvisor(42, "ask/one", { ACNO: 42, Account: "DECLINE", Pairs: [{ SYM: "EURUSD", Decision: "ACCEPT" }] }, { idempotencyKey: "decide-one" });
  await a.undoAdvisor(42, { ACNO: 42 }, { idempotencyKey: "undo-one" });

  expect(calls.map(x => x.method)).toEqual(["GET", "POST", "GET", "PUT", "GET", "GET", "PATCH", "POST"]);
  expect(calls.map(x => x.url.pathname)).toEqual([
    "/v1/fxs/accounts/42/advisor/preview", "/v1/fxs/accounts/42/advisor/asks", "/v1/fxs/accounts/42/advisor/settings",
    "/v1/fxs/accounts/42/advisor/settings", "/v1/fxs/accounts/42/advisor/asks", "/v1/fxs/accounts/42/advisor/asks/ask%2Fone",
    "/v1/fxs/accounts/42/advisor/asks/ask%2Fone", "/v1/fxs/accounts/42/advisor/undo",
  ]);
  expect(calls[0]?.url.search).toBe("?kind=PLAN&syms=EURUSD&syms=XAUUSD");
  expect(calls[4]?.url.searchParams.get("PageNo")).toBe("2");
  expect(calls[5]?.url.search).toBe("?sent=true");
  expect(calls[1]?.body).toEqual({ ACNO: 42, PreviewId: "preview", Kind: "PLAN" });
  expect(calls[6]?.body).toEqual({ ACNO: 42, Account: "DECLINE", Pairs: [{ SYM: "EURUSD", Decision: "ACCEPT" }] });
  expect(calls.filter(x => x.headers.has("Idempotency-Key")).map(x => x.headers.get("Idempotency-Key")))
    .toEqual(["send-one", "save-one", "decide-one", "undo-one"]);
  expect(calls.every(x => x.headers.get("X-Api-Key") === "owner-key")).toBe(true);
});

it("previews and reads an ask without query values when none are given", async () => {
  const { calls, accounts: a } = advisorClient();
  await a.previewAdvisor(42);
  await a.advisorHistoryView(42, "ask");
  expect(calls.map(x => x.url.search)).toEqual(["", ""]);
  expect(() => a.decideAdvisor(42, "", { ACNO: 42, Cancel: true })).toThrow(TypeError);
});
