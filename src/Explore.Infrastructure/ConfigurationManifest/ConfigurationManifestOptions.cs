namespace Explore.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Application;
using Microsoft.Extensions.Options;

public sealed class ConfigurationManifestOptions
{
    public const string ModeEnvironmentVariable = ConfigurationManifestBootstrapDeployment.ModeVariable;
    public const string PathEnvironmentVariable = ConfigurationManifestBootstrapDeployment.PathVariable;
    public const string ConventionPath =
        ConfigurationManifestBootstrapDeployment.ConventionPath;

    public ConfigurationManifestMode Mode { get; set; } = ConfigurationManifestMode.Off;

    public string? Path { get; set; }

    public static ConfigurationManifestMode ParseMode(string? value) => ConfigurationManifestBootstrapDeployment.ParseMode(value);
}

public sealed class ConfigurationManifestOptionsValidator
    : IValidateOptions<ConfigurationManifestOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ConfigurationManifestOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            return ValidateOptionsResult.Fail(
                "CONFIGURATION_MANIFEST_MODE must be Off, ValidateOnly, or Bootstrap.");
        }

        if (options.Mode == ConfigurationManifestMode.Off
            || string.IsNullOrWhiteSpace(options.Path))
        {
            return ValidateOptionsResult.Success;
        }

        return Path.IsPathFullyQualified(options.Path)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "CONFIGURATION_MANIFEST_PATH must be an absolute path.");
    }
}
