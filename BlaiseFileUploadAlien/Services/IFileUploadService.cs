namespace BlaiseFileUploadAlien.Services;

public enum FileUploadStatus
{
    Success,
    InvalidFile,
    Error
}

public interface IFileUploadService
{
    /// <summary>
    /// Uploads a file stream to GCP storage with retry and fallback logic.
    /// </summary>
    /// <param name="fileStream">The file data stream to upload</param>
    /// <param name="caseId">The case/file identifier</param>
    /// <param name="fileMeta">Metadata for the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Upload status and filename if successful; null filename if failed</returns>
    Task<(FileUploadStatus Status, string? Filename)> UploadFileAsync(
        Stream fileStream,
        int caseId,
        string fileMeta,
        CancellationToken cancellationToken);
}
