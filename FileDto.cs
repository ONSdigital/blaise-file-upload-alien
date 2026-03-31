using System.ComponentModel.DataAnnotations;

namespace BlaiseFileUploadAlien
{
    public class FileDto
    {
        [Required]
        public int Id { get; set; }
        
        public string FileMeta { get; set; } = string.Empty;
        
        [Required]
        public int[] File { get; set; } = []; 
    }
}
