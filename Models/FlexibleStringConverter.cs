using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ehiptv_msx.Models;

/// <summary>
/// O Xtream Codes alterna entre string e number nos mesmos campos
/// (ex.: "parent_id": 0 vs "parent_id": "0"). Este conversor aceita os dois.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var integer))
                {
                    return integer.ToString(CultureInfo.InvariantCulture);
                }

                return reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Null:
                return null;

            default:
                throw new JsonException($"Token inesperado ao ler string: {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
