namespace Explore.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Microsoft.Extensions.Options;

public sealed class ConfigurationManifestOptions
{
    public const string ModeEnvironmentVariable = "CONFIGURATION_MANIFEST_MODE";
    public const string PathEnvironmentVariable = "CONFIGURATION_MANIFEST_PATH";
    public const string ConventionPath =
        "/etc/islamu-event/bootstrap/configuration-manifest.json";

    public ConfigurationManifestMode Mode { get; set; } = ConfigurationManifestMode.Off;

    public string? Path { get; set; }

    public static ConfigurationManifestMode ParseMode(string? value) =>
        value switch
        {
            null or "" or "Off" => ConfigurationManifestMode.Off,
            "ValidateOnly" => ConfigurationManifestMode.ValidateOnly,
            "Bootstrap" => ConfigurationManifestMode.Bootstrap,
            _ => (ConfigurationManifestMode)(-1)
        };
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
