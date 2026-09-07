using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public class ImageStorageSupportServiceTests
{
    private static readonly byte[] ValidJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAQTGF2YzYyLjI4LjEwMgD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABMAAEBAAAAAAAAAAAAAAAAAAAABgEBAQAAAAAAAAAAAAAAAAAABgcQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAACAAIDASIAAhEAAxEA/9oADAMBAAIRAxEAPwCLAE1/f//Z");
    private static readonly byte[] ValidProgressiveJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wgARCAACAAIDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAB//EABUBAQEAAAAAAAAAAAAAAAAAAAYI/9oADAMBAAIQAxAAAAE5C1T/AP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EH//2Q==");
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");
    private static readonly byte[] ValidGif = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    private static readonly byte[] ValidWebp = Convert.FromBase64String(
        "UklGRhwAAABXRUJQVlA4TA8AAAAvAUAAAAcQ9Y/+ByKi/wEA");
    private static readonly byte[] ValidAnimatedWebp = Convert.FromBase64String(
        "UklGRsAAAABXRUJQVlA4WAoAAAACAAAAAQAAAQAAQU5JTQYAAAD/////AABBTk1GSAAAAAAAAAAAAAEAAAEAAGQAAAJWUDggMAAAANABAJ0BKgIAAgACADQloAJ0ugH4AAOwAP7wxAv/ILlhdcjX/yA/5Af8gP/48gAAAEFOTUZEAAAAAAAAAAAAAQAAAQAAZAAAAFZQOCAsAAAAlAEAnQEqAgACAAAANCWgAnS6AAOYAP75k2//kB//kB//kB//ID/iF3sgMAA=");

    [Test]
    public async Task ImageFileReaderService_ReadFileAsync_ReturnsFileData_WhenFileIsValid()
    {
        var bytes = ValidJpeg;
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("test.jpg");
        file.Size.Returns(3L);
        file.ContentType.Returns("image/jpeg");
        file.OpenReadStream(Arg.Any<long>()).Returns(_ => new MemoryStream(bytes));

        var service = new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.FileName).IsEqualTo("test.jpg");
        await Assert.That(result.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(result.Content.Span.SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task FileUploadData_CopiesContentAndPreservesBase64Json()
    {
        byte[] source = [1, 2, 3];
        var fileData = new FileUploadData(source, "test.jpg", "image/jpeg");

        source[0] = 9;

        await Assert.That(fileData.Content.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(fileData, options);
        await Assert.That(json).Contains("\"content\":\"AQID\"");

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<FileUploadData>(json, options);
        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.Content.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task ImageFileReaderService_ReadFileAsync_RejectsMismatchedBrowserContentTypeAndExtension()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("event-image.png");
        file.Size.Returns(bytes.Length);
        file.ContentType.Returns("image/png");
        file.OpenReadStream(Arg.Any<long>()).Returns(_ => new MemoryStream(bytes));

        var service = new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("photo.svg", "image/svg+xml")]
    [Arguments("photo.bmp", "image/bmp")]
    [Arguments("photo.avif", "image/avif")]
    public async Task ImageFileReaderService_ReadFileAsync_RejectsUnsupportedDeclaration(
        string fileName,
        string contentType)
    {
        byte[] pngBytes = ValidPng;
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns(fileName);
        file.Size.Returns(pngBytes.LongLength);
        file.ContentType.Returns(contentType);
        file.OpenReadStream(Arg.Any<long>()).Returns(new MemoryStream(pngBytes));
        var service = new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ImageFileReaderService_ReadFileAsync_SanitizesDangerousBrowserFileNameAndDoesNotLogIt()
    {
        var dangerousFileName = @"..\..\secret<script>.png";
        byte[] bytes = ValidPng;
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns(dangerousFileName);
        file.Size.Returns(bytes.LongLength);
        file.ContentType.Returns("image/png");
        file.OpenReadStream(Arg.Any<long>()).Returns(_ => new MemoryStream(bytes));
        var logger = Substitute.For<ILogger<ImageFileReaderService>>();
        var service = new ImageFileReaderService(logger);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.FileName).IsEqualTo("secret-script.png");
        logger.DidNotReceive().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Is<object>(state => LogStateContains(state, dangerousFileName)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task ImageUploadClientPolicy_BuildSafeFileName_DerivesAllowedExtensionFromContentType()
    {
        var result = ImageUploadClientPolicy.BuildSafeFileName("../avatar.svg", "image/webp");

        await Assert.That(result).IsEqualTo("avatar.webp");
    }

    [Test]
    public async Task ImageUploadClientPolicy_TryValidateImageFile_AcceptsOnlyMatchingSafeBrowserFormats()
    {
        var cases = new[]
        {
            (FileName: "photo.jpg", ContentType: "image/jpeg", Bytes: ValidJpeg),
            (FileName: "photo.jpeg", ContentType: "image/jpeg", Bytes: ValidProgressiveJpeg),
            (FileName: "photo.png", ContentType: "image/png", Bytes: ValidPng),
            (FileName: "photo.gif", ContentType: "image/gif", Bytes: ValidGif),
            (FileName: "photo.webp", ContentType: "image/webp", Bytes: ValidWebp),
            (FileName: "animation.webp", ContentType: "image/webp", Bytes: ValidAnimatedWebp)
        };

        foreach (var item in cases)
        {
            var accepted = ImageUploadClientPolicy.TryValidateImageFile(
                item.FileName,
                item.ContentType,
                item.Bytes,
                out var detectedContentType);

            await Assert.That(accepted).IsTrue();
            await Assert.That(detectedContentType).IsEqualTo(item.ContentType);
        }
    }

    [Test]
    public async Task ImageUploadClientPolicy_TryValidateImageFile_RejectsTruncatedAndActiveTailContainers()
    {
        var cases = new[]
        {
            (FileName: "photo.jpg", ContentType: "image/jpeg", Bytes: ValidJpeg),
            (FileName: "photo.png", ContentType: "image/png", Bytes: ValidPng),
            (FileName: "photo.gif", ContentType: "image/gif", Bytes: ValidGif),
            (FileName: "photo.webp", ContentType: "image/webp", Bytes: ValidWebp)
        };

        foreach (var item in cases)
        {
            byte[] truncated = item.Bytes[..^1];

            await Assert.That(ImageUploadClientPolicy.TryValidateImageFile(
                item.FileName,
                item.ContentType,
                truncated,
                out _)).IsFalse();

            foreach (var tail in new[]
                     {
                         "<svg><script>alert(1)</script></svg>"u8.ToArray(),
                         "<html><script>alert(1)</script></html>"u8.ToArray()
                     })
            {
                byte[] activeTail = [.. item.Bytes, .. tail];
                await Assert.That(ImageUploadClientPolicy.TryValidateImageFile(
                    item.FileName,
                    item.ContentType,
                    activeTail,
                    out _)).IsFalse();
            }
        }
    }

    [Test]
    public async Task ImageUploadClientPolicy_TryValidateImageFile_RejectsMalformedContainerFraming()
    {
        byte[] malformedJpeg = [.. ValidJpeg];
        malformedJpeg[2] = 0x00;
        byte[] malformedPng = [.. ValidPng];
        malformedPng[11] = 0x00;
        byte[] malformedGif = [.. ValidGif];
        malformedGif[6] = 0x00;
        malformedGif[7] = 0x00;
        byte[] malformedWebp = [.. ValidWebp];
        malformedWebp[4] ^= 0x01;

        var cases = new[]
        {
            (FileName: "photo.jpg", ContentType: "image/jpeg", Bytes: malformedJpeg),
            (FileName: "photo.png", ContentType: "image/png", Bytes: malformedPng),
            (FileName: "photo.gif", ContentType: "image/gif", Bytes: malformedGif),
            (FileName: "photo.webp", ContentType: "image/webp", Bytes: malformedWebp)
        };

        foreach (var item in cases)
        {
            await Assert.That(ImageUploadClientPolicy.TryValidateImageFile(
                item.FileName,
                item.ContentType,
                item.Bytes,
                out _)).IsFalse();
        }
    }

    [Test]
    public async Task ImageFileReaderService_ReadFileAsync_RejectsSafePrefixWithActiveTail()
    {
        byte[] activeTail = [.. ValidPng, .. "<html><script>alert(1)</script></html>"u8];
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("event-image.png");
        file.Size.Returns(activeTail.LongLength);
        file.ContentType.Returns("image/png");
        file.OpenReadStream(Arg.Any<long>()).Returns(_ => new MemoryStream(activeTail));
        var service = new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ImageUploadClientPolicy_TryValidateImageFile_RejectsMimeExtensionAndContainerMismatch()
    {
        var cases = new[]
        {
            (FileName: "photo.jpg", ContentType: "image/jpeg", Bytes: ValidPng),
            (FileName: "photo.png", ContentType: "image/png", Bytes: ValidGif),
            (FileName: "photo.gif", ContentType: "image/gif", Bytes: ValidWebp),
            (FileName: "photo.webp", ContentType: "image/webp", Bytes: ValidJpeg)
        };

        foreach (var item in cases)
        {
            await Assert.That(ImageUploadClientPolicy.TryValidateImageFile(
                item.FileName,
                item.ContentType,
                item.Bytes,
                out _)).IsFalse();
        }
    }

    [Test]
    public async Task ImageFileReaderService_ReadFileAsync_ReturnsNull_WhenFileExceedsMaxSize()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("big.jpg");
        file.Size.Returns(2048L);
        file.ContentType.Returns("image/jpeg");

        var service = new ImageFileReaderService(NullLogger<ImageFileReaderService>.Instance);

        var result = await service.ReadFileAsync(file, 1024);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ImagePreviewService_GenerateLocalPreviewFromBytes_ReturnsDataUri()
    {
        var service = new ImagePreviewService(NullLogger<ImagePreviewService>.Instance);
        var fileData = new FileUploadData(new byte[] { 1, 2, 3 }, "test.jpg", "image/jpeg");

        var result = service.GenerateLocalPreviewFromBytes(fileData);

        await Assert.That(result).IsEqualTo("data:image/jpeg;base64,AQID");
    }

    [Test]
    public async Task ImagePreviewService_GenerateLocalPreviewFromBytes_ReturnsEmpty_WhenFileDataIsNull()
    {
        var service = new ImagePreviewService(NullLogger<ImagePreviewService>.Instance);

        var result = service.GenerateLocalPreviewFromBytes(null!);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ImageContentClassifier_GetDefaultExtension_MapsKnownImageTypes()
    {
        var classifier = new ImageContentClassifier();

        await Assert.That(classifier.GetDefaultExtension("image/jpeg")).IsEqualTo(".jpg");
        await Assert.That(classifier.GetDefaultExtension("image/png")).IsEqualTo(".png");
        await Assert.That(classifier.GetDefaultExtension("application/octet-stream")).IsEqualTo(".bin");
    }

    [Test]
    public async Task ImageContentClassifier_GetFileTypeId_ClassifiesContentFamily()
    {
        var classifier = new ImageContentClassifier();

        await Assert.That(classifier.GetFileTypeId("image/png")).IsEqualTo(1);
        await Assert.That(classifier.GetFileTypeId("application/pdf")).IsEqualTo(2);
        await Assert.That(classifier.GetFileTypeId("video/mp4")).IsEqualTo(3);
        await Assert.That(classifier.GetFileTypeId("audio/mpeg")).IsEqualTo(4);
        await Assert.That(classifier.GetFileTypeId("application/octet-stream")).IsEqualTo(5);
    }

    [Test]
    public async Task StorageObjectUrlResolver_ResolvePublicImageUrl_ReturnsStableMetadataUrl_WhenGuidProvided()
    {
        var storageObjectId = Guid.NewGuid();
        var resolver = new StorageObjectUrlResolver();

        var result = resolver.ResolvePublicImageUrl(storageObjectId.ToString());

        await Assert.That(result).IsEqualTo($"/api/storageobject/{storageObjectId}/public");
    }

    [Test]
    public async Task StorageObjectUrlResolver_ResolvePublicImageUrl_PreservesExistingStorageApiPath()
    {
        var resolver = new StorageObjectUrlResolver();

        var result = resolver.ResolvePublicImageUrl("/api/storageobject/00000000-0000-0000-0000-000000000001/public");

        await Assert.That(result).IsEqualTo("/api/storageobject/00000000-0000-0000-0000-000000000001/public");
    }

    [Test]
    public async Task StorageObjectUrlResolver_ResolvePublicImageUrl_RejectsProviderObjectKeys()
    {
        var resolver = new StorageObjectUrlResolver();

        var result = resolver.ResolvePublicImageUrl("tenant/files/raw-object-key.jpg");

        await Assert.That(result).IsNull();
    }

    private static bool LogStateContains(object? state, string value)
    {
        return state.ToString()?.Contains(value, StringComparison.Ordinal) == true;
    }
}
