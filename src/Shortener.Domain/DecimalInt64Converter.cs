using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Shortener.Domain;

public sealed class DecimalInt64Converter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !long.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("Expected a decimal string within Int64 range.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
