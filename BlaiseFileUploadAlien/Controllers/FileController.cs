using BlaiseFileUploadAlien.Configuration;
using BlaiseFileUploadAlien.Models;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BlaiseFileUploadAlien.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly ILogger<FileController> _logger;
        private readonly StorageClient _storageClient;
        private readonly UploadSettings _uploadSettings;
        private readonly string _storagePath;


        public FileController(
            ILogger<FileController> logger, 
            StorageClient storageClient,
            IOptions<UploadSettings> uploadOptions)
        {
            _logger = logger;
            _storageClient = storageClient;
            _uploadSettings = uploadOptions.Value;
            _storagePath = _uploadSettings.StoragePath;

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        [HttpPost]
        public async Task<IActionResult> StoreFile([FromBody] FileDto fileDto, CancellationToken cancellationToken)
        {
            using var safeFileDto = fileDto;

            if (safeFileDto?.File == null || safeFileDto.File.Length == 0)
                return BadRequest("No file provided or file is empty.");

            try
            {
                _logger.LogInformation("Processing file upload for case {CaseId}", safeFileDto.Id);

                // Validates magic bytes and get file extension
                var (isValid, ext) = await TryValidateAndGetExtensionAsync(safeFileDto.File, cancellationToken);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid or corrupted file signature detected for case {CaseId}", safeFileDto.Id);
                    return BadRequest("Invalid file type or corrupted file.");
                }

                // Generates secure ID
                var shortId = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .Substring(0, 8);

                // Construct filename
                var fileName = $"{safeFileDto.Id}_{safeFileDto.FileMeta}_{shortId}.{ext}";

                // Save to disk/bucket
                await UploadFileToStorage(safeFileDto.File, fileName, ext, cancellationToken);

                return Content(JsonSerializer.Serialize(fileName), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file for case {CaseId}", safeFileDto.Id);
                return StatusCode(500, "Internal Server Error");
            }
        }

        private async Task UploadFileToStorage(Stream dataStream, string remoteFileName, string ext, CancellationToken cancellationToken)
        {
            string contentType = GetContentType(ext);

            for (int attempt = 1; attempt <= _uploadSettings.MaxAttempts; attempt++)
            {
                try
                {
                    dataStream.Position = 0;
                    await _storageClient.UploadObjectAsync(
                        _uploadSettings.BucketName,
                        remoteFileName,
                        contentType,
                        dataStream,
                        cancellationToken: cancellationToken
                    );

                    _logger.LogInformation($"Successfully uploaded {remoteFileName} on attempt {attempt}.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Attempt {attempt} to upload {remoteFileName} failed: {ex.Message}");
                    if (attempt == _uploadSettings.MaxAttempts)
                    {
                        await SaveFailedStreamToDiskAsync(dataStream, remoteFileName);
                        _logger.LogError(ex, "Failed to save upload file to bucket");
                        throw;
                    }
                    await Task.Delay(_uploadSettings.DelayBetweenRetriesMs, cancellationToken);
                }
            }
        }

        private async Task SaveFailedStreamToDiskAsync(Stream failedStream, string remoteFileName)
        {
            try
            {
                failedStream.Position = 0;
                var fullPath = Path.Combine(_storagePath, remoteFileName);

                await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await failedStream.CopyToAsync(fileStream);
                }

                _logger.LogInformation($"Backup successfully saved {remoteFileName} to {_storagePath}");
            }
            catch (Exception backupEx)
            {
                _logger.LogError(backupEx, "Failed to upload to Bucket and failed to dump memory stream to local disk");
            }
        }

        private static async Task<(bool IsValid, string Extension)> TryValidateAndGetExtensionAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null || stream.Length < 4) return (false, string.Empty);

            byte[] buffer = new byte[4];
            await stream.ReadExactlyAsync(buffer, 0, 4, cancellationToken);

            stream.Position = 0;

            if (buffer[0] == 0x89 && buffer[1] == 0x50) return (true, "png");
            if (buffer[0] == 0xFF && buffer[1] == 0xD8) return (true, "jpg");
            if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46) return (true, "gif");
            if (buffer[0] == 0x25 && buffer[1] == 0x50) return (true, "pdf");
            if (buffer[0] == 0x50 && buffer[1] == 0x4B) return (true, "zip");

            return (false, string.Empty);
        }

        private static string GetContentType(string extension)
        {
            return extension switch
            {
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "gif" => "image/gif",
                "pdf" => "application/pdf",
                "zip" => "application/zip",
                _ => "application/octet-stream"
            };
        }
    }
}
