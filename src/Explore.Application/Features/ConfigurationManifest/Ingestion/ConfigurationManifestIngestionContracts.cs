namespace Explore.Application.Features.ConfigurationManifest.Ingestion;

using ISLAMU.Wire.Contracts.ConfigurationPortability;

public enum ConfigurationManifestMode
{
    Off,
    ValidateOnly,
    Bootstrap
}

public sealed record ConfigurationManifestReadOptions(
    ConfigurationManifestMode Mode,
    string? ConfiguredPath);

public sealed record ConfigurationManifestReadResult(
    ConfigurationManifestV1Alpha2 Manifest,
    ConfigurationManifestMode Mode,
    string Sha256Digest,
    int ByteLength);

public interface IConfigurationManifestReader
{
    Task<ConfigurationManifestReadResult?> ReadAsync(
        ConfigurationManifestReadOptions request,
        CancellationToken cancellationToken);
}

public static class ConfigurationManifestIngestionFailureCodes
{
    public const string ModeInvalid = "configuration_manifest_mode_invalid";
    public const string PathInvalid = "configuration_manifest_path_invalid";
    public const string FileMissing = "configuration_manifest_file_missing";
    public const string FileUnreadable = "configuration_manifest_file_unreadable";
    public const string FileNotRegular = "configuration_manifest_file_not_regular";
    public const string FileSymlinkNotAllowed =
        "configuration_manifest_file_symlink_not_allowed";
    public const string Empty = "configuration_manifest_empty";
    public const string TooLarge = "configuration_manifest_too_large";
    public const string JsonInvalid = "configuration_manifest_json_invalid";
    public const string JsonLimitExceeded =
        "configuration_manifest_json_limit_exceeded";
    public const string DuplicateProperty =
        "configuration_manifest_duplicate_property";
}

public sealed class ConfigurationManifestIngestionException(
    string failureCode,
    string safeMessage,
    Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string FailureCode { get; } = failureCode;
}
