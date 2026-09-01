using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoblinStronghold.Simulation.ContentPacks;

public sealed class ContentIdJsonConverter : JsonConverter<ContentId>
{
    public override ContentId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A content ID must be a string.");
        }

        try
        {
            return ContentId.Parse(reader.GetString()!);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new JsonException("The content ID is invalid.", exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        ContentId value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}
