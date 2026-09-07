using Microsoft.AspNetCore.Components.Forms;

namespace Explore.Blazor.Client.Tests.Services;

public class ImageStorageServiceTests
{
    private static readonly byte[] ValidJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAQTGF2YzYyLjI4LjEwMgD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABMAAEBAAAAAAAAAAAAAAAAAAAABgEBAQAAAAAAAAAAAAAAAAAABgcQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAACAAIDASIAAhEAAxEA/9oADAMBAAIRAxEAPwCLAE1/f//Z");

    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageStorageService> _logger;
    private readonly ImageStorageService _service;

    public ImageStorageServiceTests()
    {
        _httpClientFactory = Substitute.For<System.Net.Http.IHttpClientFactory>();
        _logger = Substitute.For<ILogger<ImageStorageService>>();
        _service = new ImageStorageService(_httpClientFactory, _logger);
    }

    #region ReadFileAsync

    [Test]
    public async Task ReadFileAsync_ReturnsFileData_WhenFileIsValid()
    {
        // Arrange
        var bytes = ValidJpeg;
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("test.jpg");
        file.Size.Returns(bytes.LongLength);
        file.ContentType.Returns("image/jpeg");
        file.OpenReadStream(Arg.Any<long>()).Returns(_ => new MemoryStream(bytes));

        // Act
        var result = await _service.ReadFileAsync(file, 1024);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.FileName).IsEqualTo("test.jpg");
        await Assert.That(result.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(result.Content.Span.SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task ReadFileAsync_ReturnsNull_WhenFileIsNull()
    {
        // Act
        var result = await _service.ReadFileAsync(null!, 1024);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadFileAsync_ReturnsNull_WhenFileExceedsMaxSize()
    {
        // Arrange
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("big.jpg");
        file.Size.Returns(2048L);
        file.ContentType.Returns("image/jpeg");

        // Act
        var result = await _service.ReadFileAsync(file, 1024);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region UploadAndCreateRecordFromBytesAsync

    [Test]
    public async Task UploadAndCreateRecordFromBytesAsync_ReturnsFailure_WhenBffSessionMissing()
    {
        var fileData = new FileUploadData(new byte[] { 10, 20, 30 }, "test.jpg", "image/jpeg");

        // Act
        var result = await _service.UploadAndCreateRecordFromBytesAsync(fileData);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Failed to get an upload session");
    }

    [Test]
    public async Task UploadAndCreateRecordFromBytesAsync_WithUploadSession_UsesBffProxyOutsideBrowser()
    {
        var uploadClient = Substitute.For<IImageUploadClient>();
        var fileData = new FileUploadData(new byte[] { 10, 20, 30 }, "test.jpg", "image/jpeg");
        var storageId = Guid.NewGuid();
        uploadClient.GetUploadUrlAsync(fileData.FileName, fileData.ContentType, fileData.Size)
            .Returns(new ImageUploadTarget
            {
                UploadSessionId = "session-1",
                ExpiresInMinutes = 15
            });
        uploadClient.UploadViaBffProxyAsync("session-1", fileData)
            .Returns(new ImageUploadResult
            {
                Success = true,
                StorageObjectId = storageId,
                ViewUrl = $"/api/storageobject/{storageId}/public"
            });
        var service = new ImageStorageService(
            _httpClientFactory,
            _logger,
            uploadClient: uploadClient);

        var result = await service.UploadAndCreateRecordFromBytesAsync(fileData);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.StorageObjectId).IsEqualTo(storageId);
        await uploadClient.Received(1).UploadViaBffProxyAsync("session-1", fileData);
    }

    [Test]
    public async Task UploadAndCreateRecordFromBytesAsync_WhenUploadClientReturnsRawError_MapsGenericSafeError()
    {
        var uploadClient = Substitute.For<IImageUploadClient>();
        var fileData = new FileUploadData(new byte[] { 10, 20, 30 }, @"..\..\secret<script>.png", "image/png");
        uploadClient.GetUploadUrlAsync(fileData.FileName, fileData.ContentType, fileData.Size)
            .Returns(new ImageUploadTarget
            {
                UploadSessionId = "session-1",
                ExpiresInMinutes = 15
            });
        uploadClient.UploadViaBffProxyAsync("session-1", fileData)
            .Returns(new ImageUploadResult
            {
                Success = false,
                ErrorMessage = "provider secret body https://upload.example.com/object?signature=abc"
            });
        var service = new ImageStorageService(
            _httpClientFactory,
            _logger,
            uploadClient: uploadClient);

        var result = await service.UploadAndCreateRecordFromBytesAsync(fileData);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo(ImageUploadClientPolicy.GenericUploadFailureMessage);
    }

    #endregion

    #region GetImageUrlAsync

    [Test]
    public async Task GetImageUrlAsync_ReturnsPublicMetadataUrl_WhenGuidProvided()
    {
        // Arrange
        var storageObjectId = Guid.NewGuid();

        // Act
        var result = await _service.GetImageUrlAsync(storageObjectId.ToString());

        // Assert
        await Assert.That(result).IsEqualTo($"/api/storageobject/{storageObjectId}/public");
    }

    [Test]
    public async Task GetImageUrlAsync_ReturnsExistingApiUrl_WhenMetadataUrlProvided()
    {
        // Act
        var result = await _service.GetImageUrlAsync("/api/storageobject/00000000-0000-0000-0000-000000000001/public");

        // Assert
        await Assert.That(result).IsEqualTo("/api/storageobject/00000000-0000-0000-0000-000000000001/public");
    }

    [Test]
    public async Task GetImageUrlAsync_ReturnsNull_WhenKeyIsEmpty()
    {
        // Act
        var result = await _service.GetImageUrlAsync(string.Empty);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GenerateLocalPreviewFromBytes

    [Test]
    public async Task GenerateLocalPreviewFromBytes_ReturnsDataUri_WhenValidData()
    {
        // Arrange
        var fileData = new FileUploadData(new byte[] { 1, 2, 3 }, "test.jpg", "image/jpeg");

        // Act
        var result = _service.GenerateLocalPreviewFromBytes(fileData);

        // Assert
        await Assert.That(result.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GenerateLocalPreviewFromBytes_ReturnsEmpty_WhenNullData()
    {
        // Act
        var result = _service.GenerateLocalPreviewFromBytes(null!);

        // Assert
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    #endregion

    #region DeleteImageAsync

    [Test]
    public async Task DeleteImageAsync_ReturnsFalse_Always()
    {
        // Act
        var result = await _service.DeleteImageAsync("images/test.jpg");

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

}
