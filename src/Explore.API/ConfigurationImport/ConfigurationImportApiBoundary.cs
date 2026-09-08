namespace Explore.API.ConfigurationImport;

using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class ConfigurationImportApiBoundary
{
    public const int MaximumUploadBytes =
        ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes;
    public const string AccessTokenHeader =
        "X-Configuration-Import-Token";
    public const string UploadRateLimitPolicy =
        "ConfigurationImportUpload";
    public const string UploadRequestTimeoutPolicy =
        "ConfigurationImportUpload";
}

public sealed record ConfigurationImportProblem(
    int Status,
    string Code,
    int? RetryAfterSeconds);
