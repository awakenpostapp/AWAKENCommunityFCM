using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommunityFootballClubManager.Services.Online;

/// <summary>
/// Accepts both proper JSON booleans and legacy SQLite/D1 0/1 values.
/// New requests are always written as true/false.
/// </summary>
public sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
{
    public override bool Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number when reader.TryGetInt64(out var value) && value == 1 => true,
        JsonTokenType.Number when reader.TryGetInt64(out var value) && value == 0 => false,
        JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
        JsonTokenType.String when string.Equals(reader.GetString(), "1", StringComparison.Ordinal) => true,
        JsonTokenType.String when string.Equals(reader.GetString(), "0", StringComparison.Ordinal) => false,
        _ => throw new JsonException("Expected a boolean or a legacy 0/1 boolean value.")
    };

    public override void Write(
        Utf8JsonWriter writer,
        bool value,
        JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
