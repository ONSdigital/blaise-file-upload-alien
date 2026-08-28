using BlaiseFileUploadAlien.Configuration;
using BlaiseFileUploadAlien.Services;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;

namespace BlaiseFileUploadAlien.Tests.Services;

public class GcpFileDeletionServiceTests
{
    private readonly Mock<ILogger<GcpFileDeletionService>> _mockLogger;
    private readonly Mock<StorageClient> _mockStorageClient;

    public GcpFileDeletionServiceTests()
    {
        _mockLogger = new Mock<ILogger<GcpFileDeletionService>>();
        _mockStorageClient = new Mock<StorageClient>();
    }

    [Fact]
    public async Task DeleteFileAsync_WhenDeleteSucceeds_ReturnsDeleted()
    {
        var sut = BuildSutWithBucket("test-bucket");

        _mockStorageClient
            .Setup(s => s.DeleteObjectAsync(
                "test-bucket",
                "existing-file.txt",
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.DeleteFileAsync("existing-file.txt", CancellationToken.None);

        Assert.Equal(DeleteFileResult.Deleted, result);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenFileDoesNotExist_ReturnsNotFound()
    {
        var sut = BuildSutWithBucket("test-bucket");

        _mockStorageClient
            .Setup(s => s.DeleteObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException("storage", "Not Found")
            {
                HttpStatusCode = HttpStatusCode.NotFound
            });

        var result = await sut.DeleteFileAsync("missing-file.txt", CancellationToken.None);

        Assert.Equal(DeleteFileResult.NotFound, result);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenStorageClientThrowsUnexpectedException_ReturnsError()
    {
        var sut = BuildSutWithBucket("test-bucket");

        _mockStorageClient
            .Setup(s => s.DeleteObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("network failure"));

        var result = await sut.DeleteFileAsync("any-file.txt", CancellationToken.None);

        Assert.Equal(DeleteFileResult.Error, result);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenBucketNameIsMissing_ReturnsErrorAndSkipsStorageCall()
    {
        var sut = BuildSutWithBucket(string.Empty);

        var result = await sut.DeleteFileAsync("file.txt", CancellationToken.None);

        Assert.Equal(DeleteFileResult.Error, result);
        _mockStorageClient.Verify(
            s => s.DeleteObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenDeleteSucceeds_CallsStorageClientWithConfiguredBucketAndFilename()
    {
        var sut = BuildSutWithBucket("configured-bucket");

        _mockStorageClient
            .Setup(s => s.DeleteObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.DeleteFileAsync("report.pdf", CancellationToken.None);

        _mockStorageClient.Verify(
            s => s.DeleteObjectAsync(
                "configured-bucket",
                "report.pdf",
                It.IsAny<DeleteObjectOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private GcpFileDeletionService BuildSutWithBucket(string bucketName)
    {
        var options = Options.Create(new UploadSettings
        {
            BucketName = bucketName
        });

        return new GcpFileDeletionService(
            _mockLogger.Object,
            _mockStorageClient.Object,
            options);
    }
}
