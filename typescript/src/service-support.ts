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
 * Reads every page of a page-numbered list, advancing `pageNo` from the one asked for (1 when unset). It stops after the
 * last page the paging block names, or at the first empty page; an empty page is not yielded.
 */
export async function* pagesByPageNo<TQuery extends { pageNo?: number | null }, TPage extends PagedReply>(
  fetchPage: (query: TQuery) => Promise<TPage>,
  query: TQuery,
): AsyncGenerator<TPage, void, undefined> {
  let pageNo = query.pageNo ?? 1;
  while (true) {
    const page = await fetchPage({ ...query, pageNo });
    if (!page.data?.length) return;
    yield page;
    const shown = page.paging?.pageNo ?? pageNo;
    const count = page.paging?.pageCount;
    if (typeof count === "number" && shown >= count) return;
    pageNo = shown + 1;
  }
}

/**
 * Reads every page of an offset list, moving `offset` on by the rows each page returned. It stops at the first empty page,
 * or when `isLast` (read from the page's own paging fields) says the page was the last; an empty page is not yielded.
 */
export async function* pagesByOffset<TQuery extends { offset?: number | null }, TPage extends { data?: readonly unknown[] | null }>(
  fetchPage: (query: TQuery) => Promise<TPage>,
  query: TQuery,
  isLast: (page: TPage) => boolean = () => false,
): AsyncGenerator<TPage, void, undefined> {
  let offset = query.offset ?? 0;
  while (true) {
    const page = await fetchPage({ ...query, offset });
    const rows = page.data?.length ?? 0;
    if (!rows) return;
    yield page;
    if (isLast(page)) return;
    offset += rows;
  }
}

/** The rows of every page, one at a time. */
export async function* itemsOf<TPage extends { data?: readonly unknown[] | null }>(
  pages: AsyncIterable<TPage>,
): AsyncGenerator<FinItem<TPage>, void, undefined> {
  for await (const page of pages) for (const row of page.data ?? []) yield row as FinItem<TPage>;
}
