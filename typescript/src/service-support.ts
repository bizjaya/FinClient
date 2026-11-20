/** Helpers the generated FinGraph, FinNews and FinSocial resources share. */

/** The row type of a reply whose `data` is an array. */
export type FinItem<TPage> = TPage extends { data?: readonly (infer TItem)[] | null } ? TItem : never;

/** Encodes one path segment; an empty one is refused rather than sent as a different route. */
export function pathSegment(value: string | number): string {
  const text = String(value);
  if (!text) throw new RangeError("A path argument cannot be empty.");
  return encodeURIComponent(text);
}

/** The paging block of a page-numbered reply. */
interface PagedReply { data?: readonly unknown[] | null; paging?: { pageNo?: number; pageCount?: number } | null }

/**
 * Reads every page of a list, advancing `page` from the one asked for (1 when unset). It stops after the last page the
 * paging block names (`pageCount`), or at the first empty page; an empty page is not yielded.
 */
export async function* pagesByPage<TQuery extends { page?: number }, TPage extends PagedReply>(
  fetchPage: (query: TQuery) => Promise<TPage>,
  query: TQuery,
): AsyncGenerator<TPage, void, undefined> {
  let page = query.page ?? 1;
  while (true) {
    const reply = await fetchPage({ ...query, page });
    if (!reply.data?.length) return;
    yield reply;
    const shown = reply.paging?.pageNo ?? page;
    const count = reply.paging?.pageCount;
    if (typeof count === "number" && shown >= count) return;
    page = shown + 1;
  }
}

/** The rows of every page, one at a time. */
export async function* itemsOf<TPage extends { data?: readonly unknown[] | null }>(
  pages: AsyncIterable<TPage>,
): AsyncGenerator<FinItem<TPage>, void, undefined> {
  for await (const page of pages) for (const row of page.data ?? []) yield row as FinItem<TPage>;
}
