import { expect, it, vi } from 'vitest';
import { FinClient, type FinCopyDto } from '../src/index.js';

const Values: FinCopyDto<'DtoAIBotValues'> = {
  Numbers: { 'SYMSTG.BNLOT': { Mode: 'RANGE', Val: .02, Min: .01, Max: .1 } },
  Signals: {}, Trends: { 'SigWarm.BuyTrd': { Mode: 'STRAT', Val: null, Min: null, Max: 'NU' } }, Confidence: {},
  Methods: { BAMODE: { Mode: 'STRAT', Val: 'WDCA', Opts: ['WDCA', 'GDCA'] } }, NewsFilter: { Mode: 'FIXED', Val: false },
};

it.each(['account', 'pair'] as const)('preserves native values through a %s patch and response', async scope => {
  let sent: unknown;
  const fetch = vi.fn<typeof globalThis.fetch>(async (_url, init) => {
    sent = JSON.parse(String(init?.body));
    return new Response(JSON.stringify({ data: { Values }, code: 200, isOk: true }), { headers: { 'Content-Type': 'application/json' } });
  });
  const client = new FinClient({ apiKey: 'fixture', fetch });
  const result = scope === 'account'
    ? await client.copy.accounts.patchSettings(123, { Values })
    : await client.copy.accounts.patchPair(123, 45, { Values });
  expect(sent).toEqual(JSON.parse(JSON.stringify({ Values }, (_key, value) => value === null ? undefined : value)));
  expect(result.Values).toEqual(Values);
  expect(result.Values?.Trends?.['SigWarm.BuyTrd'].Val).toBeNull();
  expect(result.Values?.NewsFilter?.Val).toBe(false);
});
