using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Fin;

/// <summary>Walks the pages of FinGraph, FinNews and FinSocial lists.</summary>
internal static class FinPaging
{
    /// <summary>
    /// Reads every page of a page-numbered list, advancing the page number from the one in the query (1 when unset). It
    /// stops after the last page the paging block names, or at the first empty page, which it does not yield. The caller's
    /// query is copied, never changed.
    /// </summary>
    internal static async IAsyncEnumerable<TPage> ByPageNumber<TQuery, TPage, TItem>(TQuery query,
        Func<TQuery, CancellationToken, Task<TPage>> fetchPage, Func<TQuery, int?> pageNoOf, Action<TQuery, int> setPageNo,
        Func<TPage, IEnumerable<TItem>?> rows, Func<TPage, int?> shownPage, Func<TPage, int?> pageCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TQuery : class
    {
        var pageNo = pageNoOf(query) ?? 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copy = Clone(query);
            setPageNo(copy, pageNo);
            var page = await fetchPage(copy, cancellationToken).ConfigureAwait(false);
            if (rows(page)?.Any() != true) yield break;
            yield return page;
            var shown = shownPage(page) ?? pageNo;
            if (pageCount(page) is { } count && shown >= count) yield break;
            pageNo = shown + 1;
        }
    }

    /// <summary>
    /// Reads every page of an offset list, moving the offset on by the rows each page returned. It stops at the first empty
    /// page, which it does not yield, or when <paramref name="isLast"/> says the page was the last.
    /// </summary>
    internal static async IAsyncEnumerable<TPage> ByOffset<TPage, TItem>(int offset,
        Func<int, CancellationToken, Task<TPage>> fetchPage, Func<TPage, IEnumerable<TItem>?> rows, Func<TPage, bool>? isLast,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(offset, cancellationToken).ConfigureAwait(false);
            var count = rows(page)?.Count() ?? 0;
            if (count == 0) yield break;
            yield return page;
            if (isLast?.Invoke(page) == true) yield break;
            offset += count;
        }
    }

    /// <summary>The rows of every page, one at a time.</summary>
    internal static async IAsyncEnumerable<TItem> Items<TPage, TItem>(IAsyncEnumerable<TPage> pages,
        Func<TPage, IEnumerable<TItem>?> rows, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
            foreach (var row in rows(page) ?? Enumerable.Empty<TItem>())
                yield return row;
    }

    private static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, FinHttpTransport.WriteOptions), FinHttpTransport.ReadOptions)!;
}
