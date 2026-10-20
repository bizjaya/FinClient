using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fin;

/// <summary>The data of a successful reply; the transport has already checked <c>isOk</c> and that <c>data</c> is present.</summary>
internal sealed class FinApiEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>A paged reply. <c>errors</c> stays raw so an unexpected element shape never fails the read.</summary>
internal sealed class FinApiPagedEnvelope<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }

    [JsonPropertyName("paging")]
    public Fin.Copy.ResPage? Paging { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("isOk")]
    public bool IsOk { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errors")]
    public JsonElement Errors { get; set; }

    [JsonPropertyName("chksum")]
    public string? Chksum { get; set; }
}
