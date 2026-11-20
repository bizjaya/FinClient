using System.Runtime.CompilerServices;

namespace Fin;

/// <summary>Walks the pages of FinGraph, FinNews and FinSocial lists.</summary>
internal static class FinPaging
{
    /// <summary>
    /// Reads every page of a list, advancing the page number from the one given (1 when unset). It stops after the last page
    /// the paging block names, or at the first empty page, which it does not yield.
    /// </summary>
    internal static async IAsyncEnumerable<TPage> ByPage<TPage, TItem>(int firstPage,
        Func<int, CancellationToken, Task<TPage>> fetchPage, Func<TPage, IEnumerable<TItem>?> rows,
        Func<TPage, int?> shownPage, Func<TPage, int?> pageCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pageNo = firstPage;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(pageNo, cancellationToken).ConfigureAwait(false);
            if (rows(page)?.Any() != true) yield break;
            yield return page;
            var shown = shownPage(page) ?? pageNo;
            if (pageCount(page) is { } count && shown >= count) yield break;
            pageNo = shown + 1;
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
}
