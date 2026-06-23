using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BlaiseFileUploadAlien.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly ILogger<FileController> _logger;
        private readonly StorageClient _storageClient;
        private readonly string _storagePath = @"C:\BlaiseFileUploads";
        private readonly string _bucketName = "ons-blaise-v2-dev-ben1-rat";

        public FileController(ILogger<FileController> logger, StorageClient storageClient)
        {
            _logger = logger;
            _storageClient = storageClient;

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        [HttpPost]
        public async Task<IActionResult> StoreFile([FromBody] FileDto fileDto)
        {
            if (fileDto?.File == null || fileDto.File.Length == 0)
                return BadRequest("No file provided or file is empty.");

            try
            {
                _logger.LogInformation("Processing file upload for case {CaseId}", fileDto.Id);

                // Validates magic bytes and get file extension
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

                // Save to disk/bucket
                await UploadFileToStorage(fileDto.File, fileName, ext);

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

        private async Task UploadFileToStorage(Stream dataStream, string remoteFileName, string ext)
        {
            int maxAttempts = 3;
            int delayBetweenRetriesMs = 5000;
            string contentType = GetContentType(ext);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    dataStream.Position = 0;
                    await _storageClient.UploadObjectAsync(
                        _bucketName,
                        remoteFileName,
                        contentType,
                        dataStream
                    );

                    _logger.LogInformation($"Successfully uploaded {remoteFileName} on attempt {attempt}.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Attempt {attempt} to upload {remoteFileName} failed: {ex.Message}");
                    if (attempt == maxAttempts)
                    {
                        await SaveFailedStreamToDiskAsync(dataStream, remoteFileName);
                        _logger.LogError(ex, "Failed to save upload file to bucket");
                        throw;
                    }
                    await Task.Delay(delayBetweenRetriesMs);
                }
            }
        }

        private async Task SaveFailedStreamToDiskAsync(Stream failedStream, string remoteFileName)
        {
            try
            {
                failedStream.Position = 0;
                var fullPath = Path.Combine(_storagePath, remoteFileName);

                using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await failedStream.CopyToAsync(fileStream);
                }

                _logger.LogInformation($"Backup failed to save {remoteFileName} to {_storagePath}");
            }
            catch (Exception backupEx)
            {
                _logger.LogError(backupEx, "Failed to upload to Bucket and failed to dump memory stream to local disk");
            }
        }

        private static bool TryValidateAndGetExtension(Stream stream, out string extension)
        {
            extension = string.Empty;

            if (stream == null || stream.Length < 4) return false;

            byte[] buffer = new byte[4];
            stream.ReadExactly(buffer, 0, 4);

            stream.Position = 0;

            if (buffer[0] == 0x89 && buffer[1] == 0x50) { extension = "png"; return true; }
            if (buffer[0] == 0xFF && buffer[1] == 0xD8) { extension = "jpg"; return true; }
            if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46) { extension = "gif"; return true; }
            if (buffer[0] == 0x25 && buffer[1] == 0x50) { extension = "pdf"; return true; }
            if (buffer[0] == 0x50 && buffer[1] == 0x4B) { extension = "zip"; return true; }
            
            return false;
        }

        private static string GetContentType(string extension)
        {
            return extension switch
            {
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "pdf" => "application/pdf",
                "zip" => "application/zip",
                _ => "application/octet-stream"
            };
        }
    }
}
