using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlaiseFileUploadAlien;

public class FileDto : IDisposable
{
    [Required]
    public int Id { get; set; }

    public string FileMeta { get; set; } = string.Empty;

    [Required]
    [JsonConverter(typeof(StreamingIntArrayToStreamConverter))]
    public Stream File { get; set; } = Stream.Null;

    public void Dispose()
    {
        File?.Dispose();
    }
}
