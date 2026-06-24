using BlaiseFileUploadAlien.Configuration;
using BlaiseFileUploadAlien.Controllers;
using BlaiseFileUploadAlien.Models;
using FluentAssertions;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace BlaiseFileUploadAlien.Tests;

public class FileControllerTests
{
    private readonly Mock<ILogger<FileController>> _mockLogger;
    private readonly Mock<StorageClient> _mockStorageClient;
    private readonly FileController _sut;

    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly byte[] JpgHeader = { 0xFF, 0xD8, 0xFF, 0xE0 };
    private static readonly byte[] GifHeader = { 0x47, 0x49, 0x46, 0x38 };
    private static readonly byte[] PdfHeader = { 0x25, 0x50, 0x44, 0x46 };
    private static readonly byte[] ZipHeader = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] BadHeader = { 0x00, 0x00, 0x00, 0x00 };

    public static IEnumerable<object[]> ValidFileTypeData =>
    [
        [ "png", PngHeader ],
        [ "jpg", JpgHeader ],
        [ "gif", GifHeader ],
        [ "pdf", PdfHeader ],
        [ "zip", ZipHeader ],
    ];

    public static IEnumerable<object[]> ContentTypeData =>
    [
        [ "image/png",        PngHeader ],
        [ "image/jpeg",       JpgHeader ],
        [ "image/gif",        GifHeader ],
        [ "application/pdf",  PdfHeader ],
        [ "application/zip",  ZipHeader ],
    ];

    public FileControllerTests()
    {
        _mockLogger = new Mock<ILogger<FileController>>();
        _mockStorageClient = new Mock<StorageClient>();

        var testSettings = Options.Create(new UploadSettings
        {
            MaxAttempts = 3,
            DelayBetweenRetriesMs = 1,
            BucketName = "dummy_bucket"
        });

        _sut = new FileController(_mockLogger.Object, _mockStorageClient.Object, testSettings);
    }

    [Fact]
    public async Task StoreFile_WhenValidPngIsUploaded_ReturnsOkAndCallsGcp()
    {
        var fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x00 };
        var fakeStream = new MemoryStream(fakePngBytes);

        var fileDto = new FileDto
        {
            Id = 123,
            FileMeta = "cat_meme",
            File = fakeStream
        };

        var result = await _sut.StoreFile(fileDto);

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.ContentType.Should().Be("application/json");
        contentResult.Content.Should().Contain("123_cat_meme");
        contentResult.Content.Should().Contain(".png");

        _mockStorageClient.Verify(x => x.UploadObjectAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            "image/png",
            It.IsAny<Stream>()
        ), Times.Once);
    }

    private static Stream BuildStream(byte[] header, int totalSize = 100)
    {
        var data = new byte[totalSize];
        Array.Copy(header, data, Math.Min(header.Length, totalSize));
        return new MemoryStream(data);
    }

    private static FileDto BuildFileDto(byte[] header, int id = 123, string fileMeta = "meta") =>
        new FileDto { File = BuildStream(header), Id = id, FileMeta = fileMeta };

    private void SetupStorageClientSuccess() =>
        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .ReturnsAsync(new GcsObject());

    private void SetupStorageClientThrows(Exception ex) =>
        _mockStorageClient
            .Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .ThrowsAsync(ex);

    private void SetupStorageClientSucceedsOnAttempt(int successOnAttempt)
    {
        var callCount = 0;
        _mockStorageClient.Setup(s => s.UploadObjectAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<Stream>())
        ).Returns(() =>
                    ++callCount < successOnAttempt
                        ? Task.FromException<GcsObject>(new Exception("Transient GCS error"))
                        : Task.FromResult(new GcsObject()));
    }

    [Fact]
    public async Task StoreFile_WhenFileDtoIsNull_ReturnsBadRequest()
    {
        var result = await _sut.StoreFile(null);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileStreamIsNull_ReturnsBadRequest()
    {
        var dto = new FileDto { File = null, Id = 1, FileMeta = "meta" };

        var result = await _sut.StoreFile(dto);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileStreamIsEmpty_ReturnsBadRequest()
    {
        var dto = new FileDto { File = new MemoryStream(Array.Empty<byte>()), Id = 1, FileMeta = "meta" };

        var result = await _sut.StoreFile(dto);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileShorterThanFourBytes_ReturnsBadRequest()
    {
        var dto = new FileDto { File = new MemoryStream(new byte[] { 0x89, 0x50 }), Id = 1, FileMeta = "meta" };

        var result = await _sut.StoreFile(dto);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid file type or corrupted file.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileHasUnrecognisedSignature_ReturnsBadRequest()
    {
        var result = await _sut.StoreFile(BuildFileDto(BadHeader));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid file type or corrupted file.", bad.Value);
    }

    [Theory]
    [MemberData(nameof(ValidFileTypeData))]
    public async Task StoreFile_WhenValidFileType_DoesNotReturnBadRequest(string _, byte[] header)
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(header));

        Assert.IsNotType<BadRequestObjectResult>(result);
    }

    [Theory]
    [MemberData(nameof(ValidFileTypeData))]
    public async Task StoreFile_WhenUploadSucceeds_ReturnsContentResult(string _, byte[] header)
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(header));

        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_ContentTypeIsApplicationJson()
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(PngHeader));

        Assert.Equal("application/json", Assert.IsType<ContentResult>(result).ContentType);
    }

    [Theory]
    [MemberData(nameof(ValidFileTypeData))]
    public async Task StoreFile_WhenUploadSucceeds_FileNameHasCorrectExtension(string extension, byte[] header)
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(header));

        var fileName = JsonSerializer.Deserialize<string>(Assert.IsType<ContentResult>(result).Content!);
        Assert.EndsWith($".{extension}", fileName);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_FileNameContainsCaseId()
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(PngHeader, id: 99));

        var fileName = JsonSerializer.Deserialize<string>(Assert.IsType<ContentResult>(result).Content!);
        Assert.Contains("99", fileName);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_FileNameContainsFileMeta()
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(PngHeader, fileMeta: "more_cat_memes"));

        var fileName = JsonSerializer.Deserialize<string>(Assert.IsType<ContentResult>(result).Content!);
        Assert.Contains("more_cat_memes", fileName);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_FileNameContainsEightCharShortId()
    {
        SetupStorageClientSuccess();

        var result = await _sut.StoreFile(BuildFileDto(PngHeader, id: 1, fileMeta: "garfield"));

        var fileName = JsonSerializer.Deserialize<string>(Assert.IsType<ContentResult>(result).Content!);
        var nameNoExt = Path.GetFileNameWithoutExtension(fileName)!;
        var shortId = nameNoExt[(nameNoExt.LastIndexOf('_') + 1)..];
        Assert.Equal(8, shortId.Length);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_CallsStorageClientExactlyOnce()
    {
        SetupStorageClientSuccess();
        await _sut.StoreFile(BuildFileDto(PngHeader));

        _mockStorageClient.Verify(s => s.UploadObjectAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceeds_UploadsToConfiguredBucket()
    {
        SetupStorageClientSuccess();
        await _sut.StoreFile(BuildFileDto(PngHeader));

        _mockStorageClient.Verify(s => s.UploadObjectAsync(
            "dummy_bucket",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(ContentTypeData))]
    public async Task StoreFile_WhenValidFileType_UploadsWithCorrectContentType(string expectedContentType, byte[] header)
    {
        SetupStorageClientSuccess();
        await _sut.StoreFile(BuildFileDto(header));

        _mockStorageClient.Verify(s => s.UploadObjectAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            expectedContentType,
            It.IsAny<Stream>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreFile_WhenAllUploadAttemptsFail_Returns500()
    {
        SetupStorageClientThrows(new Exception("GCS unavailable"));

        var result = await _sut.StoreFile(BuildFileDto(PngHeader));

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, status.StatusCode);
        Assert.Equal("Internal Server Error", status.Value);
    }

    [Fact]
    public async Task StoreFile_WhenAllUploadAttemptsFail_RetriesExactlyThreeTimes()
    {
        SetupStorageClientThrows(new Exception("GCS unavailable"));
        await _sut.StoreFile(BuildFileDto(PngHeader));

        _mockStorageClient.Verify(s => s.UploadObjectAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceedsOnSecondAttempt_ReturnsOkResult()
    {
        SetupStorageClientSucceedsOnAttempt(successOnAttempt: 2);

        var result = await _sut.StoreFile(BuildFileDto(PngHeader));

        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task StoreFile_WhenUploadSucceedsOnSecondAttempt_CallsStorageClientTwice()
    {
        SetupStorageClientSucceedsOnAttempt(successOnAttempt: 2);
        await _sut.StoreFile(BuildFileDto(PngHeader));

        _mockStorageClient.Verify(s => s.UploadObjectAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<Stream>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task StoreFile_WhenAllUploadAttemptsFail_SavesToLocalStorage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var testSettings = Options.Create(new UploadSettings
        {
            MaxAttempts = 3,
            DelayBetweenRetriesMs = 1,
            BucketName = "dummy_bucket",
            StoragePath = tempDir
        });

        var sut = new FileController(_mockLogger.Object, _mockStorageClient.Object, testSettings);
        SetupStorageClientThrows(new Exception("GCS unavailable"));

        try
        {
            await sut.StoreFile(BuildFileDto(PngHeader));
            var savedFiles = Directory.GetFiles(tempDir);
            Assert.Single(savedFiles);
            Assert.EndsWith(".png", savedFiles[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task StoreFile_WhenAllUploadAttemptsFail_LogsAnErrorMessage()
    {
        SetupStorageClientThrows(new Exception("GCS unavailable"));

        await _sut.StoreFile(BuildFileDto(PngHeader));

        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to save upload file to bucket")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

}
