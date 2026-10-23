using Explore.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class LocalFileStorageOptionsValidatorTests
{
    [Test]
    [Arguments("public")]
    [Arguments("public/nested")]
    [Arguments("")]
    public async Task StorageRootCannotOverlapHostPublicAssets(string relativeRoot)
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"storage-web-root-{Guid.CreateVersion7():N}");
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(contentRoot);
        environment.WebRootPath.Returns(Path.Combine(contentRoot, "public"));
        var validator = new LocalFileStorageOptionsValidator(environment);
        var rejected = validator.Validate(null, new LocalFileStorageOptions
        {
            RootPath = Path.Combine(contentRoot, relativeRoot)
        });
        await Assert.That(rejected.Failed).IsTrue();
        var privateRoot = validator.Validate(null, new LocalFileStorageOptions
        {
            RootPath = Path.Combine(contentRoot, "private-storage")
        });
        await Assert.That(privateRoot.Succeeded).IsTrue();
    }

    private readonly LocalFileStorageOptionsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new LocalFileStorageOptions());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateBlankRootPathReturnsFailure()
    {
        var result = _validator.Validate(null, new LocalFileStorageOptions
        {
            RootPath = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(LocalFileStorageOptions.RootPath));
    }
}
