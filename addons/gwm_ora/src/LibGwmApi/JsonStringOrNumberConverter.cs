using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace libgwmapi;

/// <summary>
/// Reads JSON string properties that overseas gateways sometimes emit as numbers
/// (e.g. Russia acquireVehicles vehicleId).
/// </summary>
public sealed class JsonStringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            // Keep the exact decimal digits from the payload (IDs can exceed Int64).
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} when reading a string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
