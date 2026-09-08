namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Infrastructure.ConfigurationManifest;
using Microsoft.Extensions.Options;

public sealed class ConfigurationManifestOptionsValidatorTests
{
    [Test]
    public async Task Validate_ExactModesWithoutPath_AreAccepted()
    {
        var validator = new ConfigurationManifestOptionsValidator();

        foreach (ConfigurationManifestMode mode in Enum.GetValues<ConfigurationManifestMode>())
        {
            ValidateOptionsResult result = validator.Validate(
                null,
                new ConfigurationManifestOptions { Mode = mode });
            await Assert.That(result.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task Validate_UnsupportedMode_IsRejected()
    {
        var validator = new ConfigurationManifestOptionsValidator();

        ValidateOptionsResult result = validator.Validate(
            null,
            new ConfigurationManifestOptions
            {
                Mode = (ConfigurationManifestMode)99
            });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_RelativeExplicitPath_IsRejectedOutsideOffMode()
    {
        var validator = new ConfigurationManifestOptionsValidator();

        ValidateOptionsResult result = validator.Validate(
            null,
            new ConfigurationManifestOptions
            {
                Mode = ConfigurationManifestMode.Bootstrap,
                Path = "configuration-manifest.json"
            });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_OffMode_DoesNotInspectConfiguredPath()
    {
        var validator = new ConfigurationManifestOptionsValidator();

        ValidateOptionsResult result = validator.Validate(
            null,
            new ConfigurationManifestOptions
            {
                Mode = ConfigurationManifestMode.Off,
                Path = "not-an-absolute-path"
            });

        await Assert.That(result.Succeeded).IsTrue();
    }
}
