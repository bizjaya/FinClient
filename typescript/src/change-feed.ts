export interface FinChangeFeedPage<T> {
  Changes: readonly T[] | null;
  Cursor: string | null;
  More: boolean;
}

/**
 * Follow the FinCopy cursor page by page until the server reports that no more changes remain. Save each page's
 * `Cursor` after handling its changes: a restart then resumes from there instead of replaying everything.
 */
export async function* readChangePages<T>(
  readPage: (cursor?: string, signal?: AbortSignal) => Promise<FinChangeFeedPage<T>>,
  cursor?: string,
  signal?: AbortSignal,
): AsyncGenerator<FinChangeFeedPage<T>, void, undefined> {
  const seen = new Set<string>();
  if (cursor) seen.add(cursor);
  for (;;) {
    if (signal?.aborted) throw signal.reason;
    const page = await readPage(cursor, signal);
    yield page;
    if (!page.More) return;
    if (!page.Cursor || page.Cursor === cursor || seen.has(page.Cursor)) {
      throw new Error("The FinCopy change feed returned More without a new cursor.");
    }
    seen.add(page.Cursor);
    cursor = page.Cursor;
  }
}

/** Every change from every page, in order. Use `readChangePages` when the cursor must be saved between runs. */
export async function* readAllChanges<T>(
  readPage: (cursor?: string, signal?: AbortSignal) => Promise<FinChangeFeedPage<T>>,
  cursor?: string,
  signal?: AbortSignal,
): AsyncGenerator<T, void, undefined> {
  for await (const page of readChangePages(readPage, cursor, signal)) {
    for (const change of page.Changes ?? []) yield change;
  }
}
