namespace Explore.Infrastructure.Webhooks;

public interface ISvixWebhookClient
{
    Task<SvixApplicationBindingResult> GetApplicationAsync(
        string applicationId,
        CancellationToken cancellationToken);

    Task<SvixApplicationSyncResult> GetOrCreateApplicationAsync(
        SvixApplicationSyncRequest request,
        CancellationToken cancellationToken);

    Task<SvixMessageCreateResult> CreateMessageAsync(
        SvixMessageCreateRequest request,
        CancellationToken cancellationToken);

    Task<SvixMessageCreateResult> CreatePublicationMessageAsync(
        SvixProviderPublicationCreateRequest request,
        CancellationToken cancellationToken);

    Task<SvixProviderPublicationLookupResult> LookupPublicationMessageAsync(
        SvixProviderPublicationLookupRequest request,
        CancellationToken cancellationToken);

    Task<SvixAppPortalAccessResult> CreateAppPortalAccessAsync(
        SvixAppPortalAccessRequest request,
        CancellationToken cancellationToken);

    Task<SvixEventTypeSyncResult> UpsertEventTypeAsync(
        SvixEventTypeSyncRequest request,
        CancellationToken cancellationToken);
}

public sealed record SvixApplicationBindingResult(
    string AppId,
    string AppUid,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SvixApplicationSyncRequest(
    Guid TenantId,
    string AppUid,
    string Name,
    IReadOnlyDictionary<string, string> Metadata,
    string IdempotencyKey);

public sealed record SvixApplicationSyncResult(
    string AppId,
    string AppUid);

public sealed record SvixMessageCreateRequest(
    Guid TenantId,
    string AppUid,
    string EventType,
    string EventId,
    byte[] PayloadBytes,
    int PayloadRetentionDays,
    string IdempotencyKey);

public sealed record SvixMessageCreateResult(
    string MessageId);

public sealed record SvixProviderPublicationCreateRequest(
    Guid TenantId,
    string ProviderApplicationId,
    string ApplicationUid,
    string ProviderEnvironment,
    string ProviderVersion,
    string CredentialReference,
    string CredentialVersion,
    string EventType,
    string EventId,
    byte[] PayloadBytes,
    int PayloadRetentionDays,
    string IdempotencyKey,
    string RequestHash);

public sealed record SvixProviderPublicationLookupRequest(
    Guid TenantId,
    string ProviderApplicationId,
    string ApplicationUid,
    string ProviderEnvironment,
    string ProviderVersion,
    string CredentialReference,
    string CredentialVersion,
    string EventType,
    string EventId,
    string RequestHash,
    DateTime PreparedAt,
    DateTime IdempotencyValidUntil,
    int PageLimit);

public sealed record SvixProviderPublicationLookupResult(
    SvixProviderPublicationLookupOutcome Outcome,
    string? ExternalProviderMessageId,
    string? FailureCategory)
{
    public static SvixProviderPublicationLookupResult ExactMatch(string externalProviderMessageId) =>
        new(SvixProviderPublicationLookupOutcome.ExactMatch, externalProviderMessageId, null);

    public static SvixProviderPublicationLookupResult NotFound() =>
        new(SvixProviderPublicationLookupOutcome.NotFound, null, null);

    public static SvixProviderPublicationLookupResult Unavailable(string failureCategory) =>
        new(SvixProviderPublicationLookupOutcome.Unavailable, null, failureCategory);
}

public enum SvixProviderPublicationLookupOutcome
{
    ExactMatch = 1,
    NotFound = 2,
    ConflictingMatch = 3,
    Ambiguous = 4,
    Unavailable = 5,
    Unsupported = 6
}

public sealed record SvixAppPortalAccessRequest(
    string AppId,
    string SessionId,
    bool ReadOnly,
    TimeSpan ExpiresIn,
    IReadOnlyCollection<string> FeatureFlags,
    string IdempotencyKey);

public sealed record SvixAppPortalAccessResult(
    string Url,
    string? Token);

public sealed record SvixEventTypeSyncRequest(
    string Name,
    string Description,
    string GroupName,
    string SchemaJson,
    string IdempotencyKey);

public sealed record SvixEventTypeSyncResult(
    string Name);

internal sealed class SvixWebhookSubmissionException : Exception
{
    private SvixWebhookSubmissionException(
        string failureCategory,
        bool isRetryable,
        bool mayHaveBeenAccepted,
        string? safeDetail,
        Exception? innerException = null)
        : base(failureCategory, innerException)
    {
        FailureCategory = failureCategory;
        IsRetryable = isRetryable;
        MayHaveBeenAccepted = mayHaveBeenAccepted;
        SafeDetail = safeDetail;
    }

    public string FailureCategory { get; }

    public bool IsRetryable { get; }

    public bool MayHaveBeenAccepted { get; }

    public string? SafeDetail { get; }

    public static SvixWebhookSubmissionException DefinitelyNotAccepted(
        string failureCategory,
        bool isRetryable,
        string? safeDetail,
        Exception? innerException = null) =>
        new(failureCategory, isRetryable, false, safeDetail, innerException);

    public static SvixWebhookSubmissionException AcceptanceUnknown(
        string failureCategory,
        string? safeDetail,
        Exception? innerException = null) =>
        new(failureCategory, true, true, safeDetail, innerException);
}
