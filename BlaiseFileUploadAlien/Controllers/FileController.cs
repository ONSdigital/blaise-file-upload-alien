using BlaiseFileUploadAlien.Models;
using BlaiseFileUploadAlien.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BlaiseFileUploadAlien.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly IFileUploadService _fileUploadService;
        private readonly IFileDeletionService _fileDeletionService;

        public FileController(
            IFileUploadService fileUploadService,
            IFileDeletionService fileDeletionService)
        {
            _fileUploadService = fileUploadService;
            _fileDeletionService = fileDeletionService;
        }

        [HttpDelete("{filename}")]
        public async Task<IActionResult> DeleteFile([FromRoute] string filename, CancellationToken cancellationToken)
        {
            if (!IsValidFileName(filename))
            {
                return BadRequest("Filename is invalid or missing.");
            }

            var deleteResult = await _fileDeletionService.DeleteFileAsync(filename, cancellationToken);

            return deleteResult switch
            {
                DeleteFileResult.Deleted => NoContent(),
                DeleteFileResult.NotFound => NotFound("File not found."),
                _ => StatusCode(500, "Internal Server Error")
            };
        }

        [HttpPost]
        public async Task<IActionResult> StoreFile([FromBody] FileDto fileDto, CancellationToken cancellationToken)
        {
            using var safeFileDto = fileDto;

            if (safeFileDto?.File == null || safeFileDto.File.Length == 0)
                return BadRequest("No file provided or file is empty.");

            var (status, filename) = await _fileUploadService.UploadFileAsync(
                safeFileDto.File,
                safeFileDto.Id,
                safeFileDto.FileMeta,
                cancellationToken);

            return status switch
            {
                FileUploadStatus.Success => Content(JsonSerializer.Serialize(filename), "application/json"),
                FileUploadStatus.InvalidFile => BadRequest("Invalid file type or corrupted file."),
                _ => StatusCode(500, "Internal Server Error")
            };
        }



        private static bool IsValidFileName(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            if (filename.Contains("../", StringComparison.Ordinal) ||
                filename.Contains("..\\", StringComparison.Ordinal) ||
                filename.Contains('/', StringComparison.Ordinal) ||
                filename.Contains('\\', StringComparison.Ordinal))
            {
                return false;
            }

            return Path.GetFileName(filename) == filename;
        }
    }
}
