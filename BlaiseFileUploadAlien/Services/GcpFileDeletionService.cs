using BlaiseFileUploadAlien.Configuration;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using System.Net;

namespace BlaiseFileUploadAlien.Services;

public class GcpFileDeletionService : IFileDeletionService
{
    private readonly ILogger<GcpFileDeletionService> _logger;
    private readonly StorageClient _storageClient;
    private readonly UploadSettings _uploadSettings;

    public GcpFileDeletionService(
        ILogger<GcpFileDeletionService> logger,
        StorageClient storageClient,
        IOptions<UploadSettings> uploadOptions)
    {
        _logger = logger;
        _storageClient = storageClient;
        _uploadSettings = uploadOptions.Value;
    }

    public async Task<DeleteFileResult> DeleteFileAsync(string filename, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_uploadSettings.BucketName))
        {
            _logger.LogError("Bucket name is not configured for file deletion.");
            return DeleteFileResult.Error;
        }

        try
        {
            await _storageClient.DeleteObjectAsync(
                _uploadSettings.BucketName,
                filename,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted file {Filename} from bucket {BucketName}", filename, _uploadSettings.BucketName);
            return DeleteFileResult.Deleted;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {Filename} not found in bucket {BucketName}", filename, _uploadSettings.BucketName);
            return DeleteFileResult.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {Filename} from bucket {BucketName}", filename, _uploadSettings.BucketName);
            return DeleteFileResult.Error;
        }
    }
}
