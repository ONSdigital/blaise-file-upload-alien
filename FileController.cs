using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlaiseFileUploadAlien.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly ILogger<FileController> _logger;
        private readonly string _storagePath = @"C:\BlaiseFileUploads";

        public FileController(ILogger<FileController> logger)
        {
            _logger = logger;

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        [HttpPost]
        public IActionResult StoreFile([FromBody] FileDto FileDto)
        {
            if (FileDto?.File == null) return BadRequest("No file provided");

            try
            {
                _logger.LogInformation("Processing file upload for case {CaseId}", FileDto.Id);
                
                var fileBytes = FileDto.File.Select(i => (byte)i).ToArray();

                var ext = GetExtension(fileBytes);
                var shortId = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "_").Replace("+", "-").Substring(0, 8);

                // construct filename
                var fileName = $"{FileDto.Id}_{FileDto.FileMeta}_{shortId}.{ext}";
                var fullPath = Path.Combine(_storagePath, fileName);

                System.IO.File.WriteAllBytes(fullPath, fileBytes);

                _logger.LogInformation("File successfully written to {FilePath}", fullPath);

                return Content(JsonSerializer.Serialize(fileName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file for case {CaseId}", FileDto.Id);
                return StatusCode(500, "Internal Server Error");
            }
        }

        private static string GetExtension(byte[] bytes)
        {
            if (bytes.Length < 4) return "bin";
            if (bytes[0] == 0x89 && bytes[1] == 0x50) return "png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "jpg";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "gif";
            if (bytes[0] == 0x25 && bytes[1] == 0x50) return "pdf";
            if (bytes[0] == 0x50 && bytes[1] == 0x4B) return "zip";
            return "bin";
        }
    }
}
