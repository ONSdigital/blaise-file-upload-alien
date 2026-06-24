using BlaiseFileUploadAlien.Converters;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlaiseFileUploadAlien.Tests.Converters;

public class StreamingIntArrayToStreamConverterTests
{
    private class TestPayload
    {
        [JsonConverter(typeof(StreamingIntArrayToStreamConverter))]
        public Stream File { get; set; } = Stream.Null;
    }

    [Fact]
    public void Read_WhenGivenValidNumberArray_ReturnsPopulatedStream()
    {
        var json = "{\"File\": [80, 78, 71]}";

        var result = JsonSerializer.Deserialize<TestPayload>(json);

        result.Should().NotBeNull();
        result!.File.Should().NotBeNull();
        result.File.Length.Should().Be(3);

        result.File.Position.Should().Be(0);

        var buffer = new byte[3];
        result.File.ReadExactly(buffer, 0, 3);
        buffer.Should().BeEquivalentTo(new byte[] { 80, 78, 71 });
    }

    [Fact]
    public void Read_WhenNotAnArray_ThrowsJsonException()
    {
        var json = "{\"File\": \"base64_encoded_string_here\"}";

        Action act = () => JsonSerializer.Deserialize<TestPayload>(json);

        act.Should().Throw<JsonException>()
           .WithMessage("Expected a JSON array of numbers.");
    }

    [Fact]
    public void Read_WhenArrayContainsNonNumbers_ThrowsJsonException()
    {
        var json = "{\"File\": [80, \"78\", 71]}";

        Action act = () => JsonSerializer.Deserialize<TestPayload>(json);

        act.Should().Throw<JsonException>()
           .WithMessage("Expected numerical byte values.");
    }

    [Fact]
    public void Write_WhenCalled_ThrowsNotSupportedException()
    {
        using var dummyStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var payload = new TestPayload { File = dummyStream };

        Action act = () => JsonSerializer.Serialize(payload);

        act.Should().Throw<NotSupportedException>()
           .WithMessage("This converter is strictly for reading incoming file streams.*");
    }
}