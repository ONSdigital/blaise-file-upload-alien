using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlaiseFileUploadAlien;

public class StreamingIntArrayToByteArrayConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a JSON array of numbers.");
        }

        using var memoryStream = new MemoryStream();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                memoryStream.WriteByte(reader.GetByte());
            }
            else
            {
                throw new JsonException("Expected numerical byte values.");
            }
        }

        return memoryStream.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        throw new NotSupportedException("This converter is strictly for reading incoming file streams.");
    }
}
