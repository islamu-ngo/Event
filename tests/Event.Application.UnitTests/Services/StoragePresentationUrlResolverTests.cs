using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class StoragePresentationUrlResolverTests
{
    private readonly IObjectStorageService _objectStorageService = Substitute.For<IObjectStorageService>();

    public StoragePresentationUrlResolverTests()
    {
        _objectStorageService
            .GeneratePresignedDownloadUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns("https://storage.example.test/presigned");
    }

    [Test]
    public async Task ResolveImageUrlAsync_WithRawObjectKey_ReturnsNullWithoutSigning()
    {
        var result = await Resolve("tenants/example/object.png");

        await Assert.That(result).IsNull();
        await _objectStorageService.DidNotReceive().GeneratePresignedDownloadUrl(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ResolveImageUrlAsync_WithExternalUrl_ReturnsUrlWithoutSigning()
    {
        var result = await Resolve("https://cdn.example.test/images/object.png");

        await Assert.That(result).IsEqualTo("https://cdn.example.test/images/object.png");
        await _objectStorageService.DidNotReceive().GeneratePresignedDownloadUrl(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ResolveImageUrlAsync_WithLocalStorageApiPath_ReturnsPathWithoutSigning()
    {
        var path = $"/api/storageobject/{Guid.CreateVersion7()}/content";
        var expected = path.Replace("/content", "/public", StringComparison.Ordinal);

        var result = await Resolve(path);

        await Assert.That(result).IsEqualTo(expected);
        await _objectStorageService.DidNotReceive().GeneratePresignedDownloadUrl(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ResolveImageUrlAsync_WithUnsafeRelativeReference_ReturnsNullWithoutSigning()
    {
        var result = await Resolve("../secret/object.png");

        await Assert.That(result).IsNull();
        await _objectStorageService.DidNotReceive().GeneratePresignedDownloadUrl(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    private Task<string?> Resolve(string? value)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            value,
            NullLogger.Instance,
            "test image");
}
