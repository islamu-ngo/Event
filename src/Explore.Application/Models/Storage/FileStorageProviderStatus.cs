namespace Explore.Application.Models.Storage;

public sealed record FileStorageProviderStatus(
    string Provider,
    bool IsAvailable,
    bool SupportsServerSideStreaming,
    bool SupportsBrowserDirectUpload,
    string? FailureCode = null,
    string? Message = null,
    S3PreflightResult? Preflight = null);
