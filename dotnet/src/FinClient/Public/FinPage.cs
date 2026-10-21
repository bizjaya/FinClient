using Fin.Copy;

namespace Fin;

/// <summary>A page returned by a FinCopy list endpoint.</summary>
public sealed class FinPage<T>
{
    /// <summary>Rows in this page.</summary>
    public IReadOnlyList<T> Items { get; }
    /// <summary>Paging counts and current page.</summary>
    public ResPage? Paging { get; }
    /// <summary>FinCopy response code.</summary>
    public int Code { get; }
    /// <summary>Whether FinCopy accepted the request.</summary>
    public bool IsOk { get; }
    /// <summary>Optional FinCopy response message.</summary>
    public string? Message { get; }
    /// <summary>Validation or service errors returned by FinCopy.</summary>
    public IReadOnlyList<string> Errors { get; }
    /// <summary>Optional FinCopy response checksum.</summary>
    public string? Chksum { get; }

    internal FinPage(IReadOnlyList<T>? items, ResPage? paging, int code, bool isOk, string? message,
        IReadOnlyList<string>? errors, string? chksum)
    {
        Items = items ?? Array.Empty<T>();
        Paging = paging;
        Code = code;
        IsOk = isOk;
        Message = message;
        Errors = errors ?? Array.Empty<string>();
        Chksum = chksum;
    }
}

/// <summary>Common FinCopy paging and sorting inputs for account lists.</summary>
public sealed class FinPageQuery
{
    /// <summary>Requested page number.</summary>
    public int? PageNo { get; set; }
    /// <summary>Maximum rows to return.</summary>
    public int? PageSize { get; set; }
    /// <summary>Row offset.</summary>
    public int? PageOff { get; set; }
    /// <summary>Whether the selected sort is ascending.</summary>
    public bool? IsAsc { get; set; }
    /// <summary>Sort field name.</summary>
    public string? SortCol { get; set; }
    /// <summary>Sort direction.</summary>
    public string? SortDir { get; set; }
    /// <summary>Field to search.</summary>
    public string? SrchCol { get; set; }
    /// <summary>Search value for SrchCol.</summary>
    public string? SrchVal { get; set; }
    /// <summary>Search across the endpoint's searchable fields.</summary>
    public string? Search { get; set; }
    /// <summary>Sort clauses in the FinCopy page-query format.</summary>
    public IReadOnlyList<IReadOnlyList<string>>? SortBy { get; set; }
    /// <summary>Field/operator/value clauses in the FinCopy page-query format.</summary>
    public IReadOnlyList<IReadOnlyList<string>>? SrchBy { get; set; }
    /// <summary>Optional response fields requested by the endpoint.</summary>
    public IReadOnlyList<string>? Include { get; set; }

    internal string ToQueryString()
    {
        var pairs = new List<string>();
        Add("PageNo", PageNo?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("PageSize", PageSize?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("PageOff", PageOff?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("IsAsc", IsAsc?.ToString().ToLowerInvariant());
        Add("SortCol", SortCol);
        Add("SortDir", SortDir);
        Add("SrchCol", SrchCol);
        Add("SrchVal", SrchVal);
        Add("search", Search);
        AddMany("SortBy", SortBy);
        AddMany("SrchBy", SrchBy);
        if (Include is not null) foreach (var include in Include) Add("Include", include);
        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);

        void Add(string key, string? value)
        {
            if (value is not null) pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
        }

        void AddMany(string key, IReadOnlyList<IReadOnlyList<string>>? values)
        {
            if (values is null) return;
            for (var i = 0; i < values.Count; i++)
            for (var j = 0; j < values[i].Count; j++)
                Add($"{key}[{i}][{j}]", values[i][j]);
        }
    }
}
