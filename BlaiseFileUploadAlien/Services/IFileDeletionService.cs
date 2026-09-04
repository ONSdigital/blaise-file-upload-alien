namespace BlaiseFileUploadAlien.Services;

public enum DeleteFileResult
{
    Deleted,
    NotFound,
    Error
}

public interface IFileDeletionService
{
    Task<DeleteFileResult> DeleteFileAsync(string filename, CancellationToken cancellationToken);
}
