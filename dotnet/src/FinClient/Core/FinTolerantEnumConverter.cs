using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fin;

/// <summary>
/// Reads a FinCopy enum without failing on a value this version of the SDK does not know yet, so a new server value never
/// breaks an older client. An unknown value becomes the enum's <c>Unknown</c> or <c>UNK</c> member when it has one;
/// otherwise a nullable property reads it as null (never as an unrelated member) and a non-nullable one as the default.
/// The generated types reference this converter on every enum property.
/// </summary>
internal sealed class FinTolerantEnumConverter<T> : JsonConverterFactory where T : struct, Enum
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T) || typeToConvert == typeof(T?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(T) ? TolerantEnum<T>.ForValue : TolerantEnum<T>.ForNullable;
}

/// <summary>The same reading rules for any enum, for enums that carry no converter attribute.</summary>
internal sealed class FinTolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert).IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        var holder = typeof(TolerantEnum<>).MakeGenericType(underlying ?? typeToConvert);
        var field = holder.GetField(underlying is null ? nameof(TolerantEnum<DayOfWeek>.ForValue) : nameof(TolerantEnum<DayOfWeek>.ForNullable),
            BindingFlags.Public | BindingFlags.Static)!;
        return (JsonConverter)field.GetValue(null)!;
    }
}

internal static class TolerantEnum<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, T> ByNameIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<T, string> NameOf = new();
    private static readonly T Fallback;
    private static readonly bool HasUnknownMember;

    public static readonly JsonConverter<T> ForValue = new ValueConverter();
    public static readonly JsonConverter<T?> ForNullable = new NullableConverter();

    static TolerantEnum()
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (T)field.GetValue(null)!;
            var wireName = field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name;
            ByName[wireName] = value;
            ByName[field.Name] = value;
            if (!ByNameIgnoreCase.ContainsKey(wireName)) ByNameIgnoreCase[wireName] = value;
            if (!NameOf.ContainsKey(value)) NameOf[value] = wireName;
        }
        HasUnknownMember = ByName.TryGetValue("Unknown", out Fallback) || ByName.TryGetValue("UNK", out Fallback);
        if (!HasUnknownMember) Fallback = default;
    }

    /// <summary>The value read, or null when it is not one this enum knows.</summary>
    private static T? ReadKnown(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var name = reader.GetString();
                return name is not null && (ByName.TryGetValue(name, out var byName) || ByNameIgnoreCase.TryGetValue(name, out byName))
                    ? byName : null;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var number))
                {
                    var value = (T)Enum.ToObject(typeof(T), number);
                    if (NameOf.ContainsKey(value)) return value;
                }
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    private static void Write(Utf8JsonWriter writer, T value) =>
        writer.WriteStringValue(NameOf.TryGetValue(value, out var name) ? name : value.ToString());

    private sealed class ValueConverter : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadKnown(ref reader) ?? Fallback;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            TolerantEnum<T>.Write(writer, value);
    }

    private sealed class NullableConverter : JsonConverter<T?>
    {
        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return ReadKnown(ref reader) ?? (HasUnknownMember ? Fallback : null);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is { } present) TolerantEnum<T>.Write(writer, present);
            else writer.WriteNullValue();
        }
    }
}
