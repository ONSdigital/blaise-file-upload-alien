using BlaiseFileUploadAlien.Configuration;
using BlaiseFileUploadAlien.Services;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace BlaiseFileUploadAlien.Tests.Services;

public class GcpFileUploadServiceTests
{
    private readonly Mock<ILogger<GcpFileUploadService>> _mockLogger;
    private readonly Mock<StorageClient> _mockStorageClient;

    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly byte[] JpgHeader = { 0xFF, 0xD8, 0xFF, 0xE0 };
    private static readonly byte[] GifHeader = { 0x47, 0x49, 0x46, 0x38 };
    private static readonly byte[] PdfHeader = { 0x25, 0x50, 0x44, 0x46 };
    private static readonly byte[] ZipHeader = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] BadHeader = { 0x00, 0x00, 0x00, 0x00 };

    public static IEnumerable<object[]> ValidFileTypesData =>
    [
        [ PngHeader, "png" ],
        [ JpgHeader, "jpg" ],
        [ GifHeader, "gif" ],
        [ PdfHeader, "pdf" ],
        [ ZipHeader, "zip" ],
    ];

    public GcpFileUploadServiceTests()
    {
        _mockLogger = new Mock<ILogger<GcpFileUploadService>>();
        _mockStorageClient = new Mock<StorageClient>();
    }

    [Fact]
    public async Task UploadFileAsync_WhenValidPngIsUploaded_ReturnsSuccessWithFilename()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(PngHeader);

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .ReturnsAsync(new GcsObject());

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.Success, status);
        Assert.NotNull(filename);
        Assert.Contains("123_receipt", filename);
        Assert.EndsWith(".png", filename);
    }

    [Theory]
    [MemberData(nameof(ValidFileTypesData))]
    public async Task UploadFileAsync_WhenValidFileTypeIsUploaded_ReturnsSuccessWithCorrectExtension(byte[] header, string expectedExt)
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(header);

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .ReturnsAsync(new GcsObject());

        var (status, filename) = await sut.UploadFileAsync(fileStream, 1, "meta", CancellationToken.None);

        Assert.Equal(FileUploadStatus.Success, status);
        Assert.EndsWith($".{expectedExt}", filename);
    }

    [Fact]
    public async Task UploadFileAsync_WhenFileHasInvalidSignature_ReturnsInvalidFileAndNoUpload()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(BadHeader);

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.InvalidFile, status);
        Assert.Null(filename);
        _mockStorageClient.Verify(
            s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_WhenFileIsTooSmall_ReturnsInvalidFileAndNoUpload()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = new MemoryStream(new byte[] { 0x89, 0x50 });

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.InvalidFile, status);
        Assert.Null(filename);
    }

    [Fact]
    public async Task UploadFileAsync_WhenUploadSucceeds_CallsStorageClientWithConfiguredBucketAndProperMetadata()
    {
        var sut = BuildSutWithBucket("my-bucket", "C:\\temp");
        var fileStream = BuildStream(PngHeader);

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .ReturnsAsync(new GcsObject());

        await sut.UploadFileAsync(fileStream, 456, "expense", CancellationToken.None);

        _mockStorageClient.Verify(
            s => s.UploadObjectAsync(
                "my-bucket",
                It.Is<string>(f => f.StartsWith("456_expense_")),
                "image/png",
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_WhenUploadFailsOnceButSucceedsOnSecondAttempt_ReturnsSuccess()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(PngHeader);
        var callSequence = new Queue<Func<Task<GcsObject>>>(new[]
        {
            () => Task.FromException<GcsObject>(new Exception("Transient error")),
            () => Task.FromResult(new GcsObject())
        });

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .Returns(() => callSequence.Dequeue()());

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.Success, status);
        Assert.NotNull(filename);
        _mockStorageClient.Verify(
            s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadFileAsync_WhenAllUploadAttemptsFail_ReturnsErrorAfterThreeAttempts()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(PngHeader);

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .ThrowsAsync(new Exception("Network failure"));

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.Error, status);
        Assert.Null(filename);
        _mockStorageClient.Verify(
            s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task UploadFileAsync_WhenBucketNameIsMissing_ReturnsError()
    {
        var sut = BuildSutWithBucket(string.Empty, "C:\\temp");
        var fileStream = BuildStream(PngHeader);

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        Assert.Equal(FileUploadStatus.Error, status);
        Assert.Null(filename);
    }

    [Fact]
    public async Task UploadFileAsync_WhenUploadSucceeds_GeneratedFilenameContainsEightCharShortId()
    {
        var sut = BuildSutWithBucket("test-bucket", "C:\\temp");
        var fileStream = BuildStream(PngHeader);

        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<UploadObjectOptions>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<IUploadProgress>>()))
            .ReturnsAsync(new GcsObject());

        var (status, filename) = await sut.UploadFileAsync(fileStream, 123, "receipt", CancellationToken.None);

        var nameNoExt = Path.GetFileNameWithoutExtension(filename!);
        var shortId = nameNoExt.Split("123_receipt_")[1];
        Assert.Equal(8, shortId.Length);
    }

    private static Stream BuildStream(byte[] header, int totalSize = 100)
    {
        var data = new byte[totalSize];
        Array.Copy(header, data, Math.Min(header.Length, totalSize));
        return new MemoryStream(data);
    }

    private GcpFileUploadService BuildSutWithBucket(string bucketName, string storagePath)
    {
        var options = Options.Create(new UploadSettings
        {
            BucketName = bucketName,
            MaxAttempts = 3,
            DelayBetweenRetriesMs = 1,
            StoragePath = storagePath
        });

        return new GcpFileUploadService(
            _mockLogger.Object,
            _mockStorageClient.Object,
            options);
    }
}
