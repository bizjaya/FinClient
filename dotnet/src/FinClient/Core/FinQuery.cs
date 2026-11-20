using System.Globalization;
using System.Text.Json;

namespace Fin;

/// <summary>Builds the paths and query strings of FinGraph, FinNews and FinSocial requests.</summary>
internal static class FinQuery
{
    /// <summary>Encodes one path segment; an empty one is refused rather than sent as a different route.</summary>
    internal static string Segment(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("A path argument cannot be empty.", nameof(value));
        return Uri.EscapeDataString(value);
    }

    /// <summary>The path followed by the query values that are set. Null values are left out; dates are sent as UTC ISO 8601.</summary>
    internal static string Path(string path, params (string Name, object? Value)[] query)
    {
        var pairs = new List<string>();
        foreach (var (name, value) in query)
        {
            var text = Text(value);
            if (text is not null) pairs.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text));
        }
        return pairs.Count == 0 ? path : path + "?" + string.Join("&", pairs);
    }

    /// <summary>A query object as the one JSON value of a <c>qry</c> parameter. Unset (null) members are left out.</summary>
    internal static string Json(object qry) => JsonSerializer.Serialize(qry, qry.GetType(), FinHttpTransport.WriteOptions);

    private static string? Text(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        DateTime d => d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        // An enum goes out under the name the API uses for it (its EnumMember value), the way the DTOs write it.
        Enum e => JsonSerializer.Serialize(e, e.GetType(), FinHttpTransport.WriteOptions).Trim('"'),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
