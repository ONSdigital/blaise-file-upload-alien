using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;

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
        public IActionResult StoreFile([FromBody] FileDto fileDto)
        {
            if (fileDto?.File == null || fileDto.File.Length == 0)
                return BadRequest("No file provided or file is empty.");

            try
            {
                _logger.LogInformation("Processing file upload for case {CaseId}", fileDto.Id);

                // Validates magic bytes
                if (!TryValidateAndGetExtension(fileDto.File, out string ext))
                {
                    _logger.LogWarning("Invalid or corrupted file signature detected for case {CaseId}", fileDto.Id);
                    return BadRequest("Invalid file type or corrupted file.");
                }

                // Generates secure ID
                var shortId = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Substring(0, 8);

                // Construct filename
                var fileName = $"{fileDto.Id}_{fileDto.FileMeta}_{shortId}.{ext}";
                var fullPath = Path.Combine(_storagePath, fileName);

                // Save to disk/bucket

                UploadFileToStorage(fileDto.File, fileName, ext);
                _logger.LogInformation("File successfully uploaded to bucket TODO");

                using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    fileDto.File.CopyTo(fileStream);
                }

                _logger.LogInformation("File successfully written to {FilePath}", fullPath);

                return Content(JsonSerializer.Serialize(fileName), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file for case {CaseId}", fileDto.Id);
                return StatusCode(500, "Internal Server Error");
            }
            finally
            {
                fileDto.Dispose(); 
            }
        }

        private bool UploadFileToStorage(Stream fileStream, string fileName, string ext)
        {
            // Automatically finds the metadata server? and pulls the token for Blaise Compute service account
            GoogleCredential defaultCredential = GoogleCredential.GetApplicationDefault();

            var impersonatedCredential = defaultCredential.Impersonate(
                new ImpersonatedCredential.Initializer("bucket-uploader-sa@ons-blaise-v2-dev-ben1.iam.gserviceaccount.com")
                {
                    Scopes = new[] { "https://www.googleapis.com/auth/devstorage.read_write" }
                }
            );

            var storageClient = StorageClient.Create(impersonatedCredential);

            storageClient.UploadObject("ons-blaise-v2-dev-ben1-rat", fileName, GetContentType(ext), fileStream);

            return true; // TODO: Handle errors and return false if upload fails
        }

        private static bool TryValidateAndGetExtension(Stream stream, out string extension)
        {
            extension = string.Empty;

            if (stream == null || stream.Length < 4) return false;

            byte[] buffer = new byte[4];
            stream.ReadExactly(buffer, 0, 4);

            stream.Position = 0;

            if (buffer == null || buffer.Length < 4) return false;
            if (buffer[0] == 0x89 && buffer[1] == 0x50) { extension = "png"; return true; }
            if (buffer[0] == 0xFF && buffer[1] == 0xD8) { extension = "jpg"; return true; }
            if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46) { extension = "gif"; return true; }
            if (buffer[0] == 0x25 && buffer[1] == 0x50) { extension = "pdf"; return true; }
            if (buffer[0] == 0x50 && buffer[1] == 0x4B) { extension = "zip"; return true; }
            
            return false;
        }

        private string GetContentType(string extension)
        {
            return extension switch
            {
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "pdf" => "application/pdf",
                "zip" => "application/zip",

                // If file type is not recognised, tell GCP it is generic binary data
                _ => "application/octet-stream"
            };
        }
    }
}
