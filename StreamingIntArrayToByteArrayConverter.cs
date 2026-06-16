using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IO;

namespace BlaiseFileUploadAlien;

public class StreamingIntArrayToStreamConverter : JsonConverter<Stream>
{
    private static readonly RecyclableMemoryStreamManager.Options _poolOptions = new()
    {
        BlockSize = 128 * 1024,
        MaximumSmallPoolFreeBytes = 40 * 1024 * 1024,
        MaximumLargePoolFreeBytes = 40 * 1024 * 1024,
    };

    private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new(_poolOptions);

    public override Stream Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a JSON array of numbers.");
        }

        var stream = _memoryStreamManager.GetStream();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                stream.WriteByte(reader.GetByte());
            }
            else
            {
                throw new JsonException("Expected numerical byte values.");
            }
        }

        stream.Position = 0;

        return stream;
    }

    public override void Write(Utf8JsonWriter writer, Stream value, JsonSerializerOptions options)
    {
        throw new NotSupportedException("This converter is strictly for reading incoming file streams.");
    }
}
