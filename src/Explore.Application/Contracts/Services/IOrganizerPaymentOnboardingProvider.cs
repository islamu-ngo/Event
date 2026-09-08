namespace Explore.Application.Contracts.Services;

public interface IOrganizerPaymentOnboardingProvider
{
    Task<OrganizerPaymentProviderAccountCreationResult> CreateAccountAsync(
        OrganizerPaymentProviderAccountCreationRequest request,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentOnboardingLinkCreationResult> CreateOnboardingLinkAsync(
        OrganizerPaymentOnboardingLinkRequest request,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentProviderReadinessResult> GetReadinessAsync(
        OrganizerPaymentProviderReadinessRequest request,
        CancellationToken cancellationToken);
}

public sealed record OrganizerPaymentProviderAccountCreationRequest(
    Guid TenantId,
    Guid OrganizerActorId,
    string ProviderCode,
    string ConnectPlatformId,
    string ProviderIdempotencyKey);

public sealed record OrganizerPaymentOnboardingLinkRequest(
    string ProviderCode,
    string ConnectPlatformId,
    string ExternalAccountId,
    Uri ReturnUrl,
    Uri RefreshUrl,
    OrganizerPaymentOnboardingType OnboardingType);

public sealed record OrganizerPaymentProviderReadinessRequest(
    string ProviderCode,
    string ConnectPlatformId,
    string ExternalAccountId);

public sealed record OrganizerPaymentProviderAccountCreationResult(
    OrganizerPaymentProviderAccountCreationStatus Status,
    string? ExternalAccountId,
    string? FailureCode = null,
    OrganizerPaymentProviderFailureKind FailureKind = OrganizerPaymentProviderFailureKind.None,
    string? ProviderRequestId = null)
{
    public static OrganizerPaymentProviderAccountCreationResult Created(string externalAccountId) =>
        new(OrganizerPaymentProviderAccountCreationStatus.Created, externalAccountId);

    public static OrganizerPaymentProviderAccountCreationResult ManualReconciliationRequired(
        string failureCode = "organizer_payment_provider_manual_reconciliation_required",
        OrganizerPaymentProviderFailureKind failureKind = OrganizerPaymentProviderFailureKind.ProviderUnknown,
        string? providerRequestId = null) =>
        new(
            OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired,
            null,
            failureCode,
            failureKind,
            providerRequestId);

    public static OrganizerPaymentProviderAccountCreationResult Failed(
        string failureCode,
        OrganizerPaymentProviderFailureKind failureKind = OrganizerPaymentProviderFailureKind.ProviderRejected,
        string? providerRequestId = null) =>
        new(OrganizerPaymentProviderAccountCreationStatus.Failed, null, failureCode, failureKind, providerRequestId);
}

public sealed record OrganizerPaymentOnboardingLinkCreationResult(
    bool Success,
    Uri? OnboardingUrl,
    string? FailureCode = null,
    OrganizerPaymentProviderFailureKind FailureKind = OrganizerPaymentProviderFailureKind.None,
    string? ProviderRequestId = null)
{
    public static OrganizerPaymentOnboardingLinkCreationResult Created(Uri onboardingUrl) => new(true, onboardingUrl);
    public static OrganizerPaymentOnboardingLinkCreationResult Failed(
        string failureCode,
        OrganizerPaymentProviderFailureKind failureKind = OrganizerPaymentProviderFailureKind.ProviderRejected,
        string? providerRequestId = null) =>
        new(false, null, failureCode, failureKind, providerRequestId);
}

public sealed record OrganizerPaymentProviderReadinessResult(
    bool Success,
    OrganizerPaymentProviderReadiness? Readiness,
    string? FailureCode = null,
    OrganizerPaymentProviderFailureKind FailureKind = OrganizerPaymentProviderFailureKind.None,
    string? ProviderRequestId = null)
{
    public static OrganizerPaymentProviderReadinessResult Retrieved(
        OrganizerPaymentProviderReadiness readiness,
        string? providerRequestId = null) =>
        new(true, readiness, ProviderRequestId: providerRequestId);

    public static OrganizerPaymentProviderReadinessResult Failed(
        string failureCode,
        OrganizerPaymentProviderFailureKind failureKind = OrganizerPaymentProviderFailureKind.ProviderRejected,
        string? providerRequestId = null) =>
        new(false, null, failureCode, failureKind, providerRequestId);
}

public sealed record OrganizerPaymentProviderReadiness(
    bool ChargesEnabled,
    OrganizerPaymentProviderCapabilityState CardPaymentsCapabilityState,
    OrganizerPaymentProviderCapabilityState TransfersCapabilityState,
    OrganizerPaymentProviderRequirementsState RequirementsState,
    IReadOnlyList<string> CurrentlyDueRequirementKeys,
    IReadOnlyList<string> EventuallyDueRequirementKeys,
    IReadOnlyList<string> PastDueRequirementKeys,
    string? DisabledReason,
    string? MerchantCountryCode,
    IReadOnlyList<string> SupportedCurrencyCodes,
    DateTime ObservedAt,
    string EvidenceRevision);

public enum OrganizerPaymentProviderAccountCreationStatus
{
    Failed = 0,
    Created = 1,
    ManualReconciliationRequired = 2
}

public enum OrganizerPaymentProviderFailureKind
{
    None = 0,
    Configuration = 1,
    ProviderRejected = 2,
    ProviderUnknown = 3,
    Network = 4,
    Canceled = 5,
    ProviderDataIncomplete = 6
}

public enum OrganizerPaymentProviderCapabilityState
{
    Unknown = 0,
    Inactive = 1,
    Pending = 2,
    Active = 3
}

public enum OrganizerPaymentProviderRequirementsState
{
    Unknown = 0,
    CurrentlyDue = 1,
    EventuallyDue = 2,
    PastDue = 3,
    Disabled = 4,
    Satisfied = 5
}

public enum OrganizerPaymentOnboardingType
{
    AccountOnboarding = 1
}
