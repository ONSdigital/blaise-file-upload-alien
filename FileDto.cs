using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlaiseFileUploadAlien
{
    public class FileDto
    {
        [Required]
        public int Id { get; set; }
        
        public string FileMeta { get; set; } = string.Empty;
        
        [Required]
        [JsonConverter(typeof(StreamingIntArrayToByteArrayConverter))]
        public byte[] File { get; set; } = [];
    }
}
