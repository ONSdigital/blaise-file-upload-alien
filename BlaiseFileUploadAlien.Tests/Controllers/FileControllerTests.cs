using BlaiseFileUploadAlien.Controllers;
using BlaiseFileUploadAlien.Models;
using BlaiseFileUploadAlien.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;

namespace BlaiseFileUploadAlien.Tests;

public class FileControllerTests
{
    private readonly Mock<IFileUploadService> _mockFileUploadService;
    private readonly Mock<IFileDeletionService> _mockFileDeletionService;
    private readonly FileController _sut;

    public FileControllerTests()
    {
        _mockFileUploadService = new Mock<IFileUploadService>();
        _mockFileDeletionService = new Mock<IFileDeletionService>();

        _sut = new FileController(_mockFileUploadService.Object, _mockFileDeletionService.Object);
    }

    #region StoreFile Tests

    [Fact]
    public async Task StoreFile_WhenFileDtoIsNull_ReturnsBadRequest()
    {
        var result = await _sut.StoreFile(null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileStreamIsNull_ReturnsBadRequest()
    {
        var dto = new FileDto { File = null, Id = 1, FileMeta = "meta" };

        var result = await _sut.StoreFile(dto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenFileStreamIsEmpty_ReturnsBadRequest()
    {
        var dto = new FileDto { File = new MemoryStream(Array.Empty<byte>()), Id = 1, FileMeta = "meta" };

        var result = await _sut.StoreFile(dto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file provided or file is empty.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenServiceReturnsSuccess_ReturnsContentWithFilename()
    {
        var fileDto = new FileDto
        {
            Id = 123,
            FileMeta = "receipt",
            File = new MemoryStream(new byte[] { 0x89, 0x50 })
        };

        _mockFileUploadService
            .Setup(s => s.UploadFileAsync(
                It.IsAny<Stream>(),
                123,
                "receipt",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadStatus.Success, "123_receipt_ABC12345.png"));

        var result = await _sut.StoreFile(fileDto, CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
        
        var filename = JsonSerializer.Deserialize<string>(contentResult.Content!);
        Assert.Equal("123_receipt_ABC12345.png", filename);
    }

    [Fact]
    public async Task StoreFile_WhenServiceReturnsInvalidFile_ReturnsBadRequest()
    {
        var fileDto = new FileDto
        {
            Id = 123,
            FileMeta = "receipt",
            File = new MemoryStream(new byte[] { 0x00, 0x00 })
        };

        _mockFileUploadService
            .Setup(s => s.UploadFileAsync(
                It.IsAny<Stream>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadStatus.InvalidFile, null));

        var result = await _sut.StoreFile(fileDto, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid file type or corrupted file.", bad.Value);
    }

    [Fact]
    public async Task StoreFile_WhenServiceReturnsError_ReturnsInternalServerError()
    {
        var fileDto = new FileDto
        {
            Id = 123,
            FileMeta = "receipt",
            File = new MemoryStream(new byte[] { 0x89, 0x50 })
        };

        _mockFileUploadService
            .Setup(s => s.UploadFileAsync(
                It.IsAny<Stream>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadStatus.Error, null));

        var result = await _sut.StoreFile(fileDto, CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal("Internal Server Error", error.Value);
    }

    [Fact]
    public async Task StoreFile_WhenServiceReturnsSuccess_CallsUploadServiceWithCorrectParameters()
    {
        var fileStream = new MemoryStream(new byte[] { 0x89, 0x50 });
        var fileDto = new FileDto
        {
            Id = 456,
            FileMeta = "expense",
            File = fileStream
        };

        _mockFileUploadService
            .Setup(s => s.UploadFileAsync(
                It.IsAny<Stream>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadStatus.Success, "456_expense_XYZ98765.jpg"));

        await _sut.StoreFile(fileDto, CancellationToken.None);

        _mockFileUploadService.Verify(
            s => s.UploadFileAsync(
                fileStream,
                456,
                "expense",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeleteFile Tests

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    public async Task DeleteFile_WhenFilenameIsInvalid_ReturnsBadRequest(string fileName)
    {
        var result = await _sut.DeleteFile(fileName, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Filename is invalid or missing.", bad.Value);
    }

    [Fact]
    public async Task DeleteFile_WhenServiceReturnsDeleted_ReturnsNoContent()
    {
        _mockFileDeletionService
            .Setup(s => s.DeleteFileAsync("exists.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeleteFileResult.Deleted);

        var result = await _sut.DeleteFile("exists.txt", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteFile_WhenServiceReturnsNotFound_ReturnsNotFound()
    {
        _mockFileDeletionService
            .Setup(s => s.DeleteFileAsync("missing.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeleteFileResult.NotFound);

        var result = await _sut.DeleteFile("missing.txt", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("File not found.", notFound.Value);
    }

    [Fact]
    public async Task DeleteFile_WhenServiceReturnsError_ReturnsInternalServerError()
    {
        _mockFileDeletionService
            .Setup(s => s.DeleteFileAsync("error.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeleteFileResult.Error);

        var result = await _sut.DeleteFile("error.txt", CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal("Internal Server Error", error.Value);
    }

    [Fact]
    public async Task DeleteFile_WhenFilenameIsValid_CallsServiceWithFilename()
    {
        _mockFileDeletionService
            .Setup(s => s.DeleteFileAsync("valid-file.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeleteFileResult.Deleted);

        await _sut.DeleteFile("valid-file.pdf", CancellationToken.None);

        _mockFileDeletionService.Verify(
            s => s.DeleteFileAsync("valid-file.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}