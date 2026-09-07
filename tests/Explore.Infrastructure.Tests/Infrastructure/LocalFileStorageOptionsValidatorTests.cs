using Explore.Infrastructure.Storage;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class LocalFileStorageOptionsValidatorTests
{
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
