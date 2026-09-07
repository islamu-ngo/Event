using System.Text.Json;
using Explore.Domain;

namespace Explore.Application.Contracts.Services.Registration;

public sealed record RegistrationProviderTuple(
    string ProviderCode,
    string ProviderDeploymentCode,
    string ApiVersion,
    string AdapterPolicyVersion,
    string ConformanceEvidenceRevision)
{
    public static RegistrationProviderTuple Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    public string DeploymentKind => ProviderDeploymentCode;
    public string Key => string.Join('|', ProviderCode, ProviderDeploymentCode, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision);
}

public sealed record RegistrationProviderCapabilitySet(
    bool Redirect,
    bool Embed,
    bool Manual,
    bool SchemaRead,
    bool FormProvision,
    bool SubmissionWrite,
    bool SubmissionRead,
    bool CallbackVerification,
    bool SubscriptionManagement,
    bool Reconciliation,
    bool SubmissionSink,
    bool AutoFinalize)
{
    public static RegistrationProviderCapabilitySet None { get; } = new(false, false, false, false, false, false, false, false, false, false, false, false);
    public static RegistrationProviderCapabilitySet Native { get; } = new(true, false, true, true, true, true, true, false, false, false, true, true);

    public RegistrationProviderCapabilitySet Intersect(RegistrationProviderCapabilitySet other) => new(
        Redirect && other.Redirect,
        Embed && other.Embed,
        Manual && other.Manual,
        SchemaRead && other.SchemaRead,
        FormProvision && other.FormProvision,
        SubmissionWrite && other.SubmissionWrite,
        SubmissionRead && other.SubmissionRead,
        CallbackVerification && other.CallbackVerification,
        SubscriptionManagement && other.SubscriptionManagement,
        Reconciliation && other.Reconciliation,
        SubmissionSink && other.SubmissionSink,
        AutoFinalize && other.AutoFinalize);

    public static RegistrationProviderCapabilitySet FromCodes(IEnumerable<string> codes)
    {
        HashSet<string> values = [.. codes.Select(code => code.Trim().ToUpperInvariant())];
        return new(
            values.Contains(RegistrationProviderCapabilityCodes.Redirect),
            values.Contains(RegistrationProviderCapabilityCodes.Embed),
            values.Contains(RegistrationProviderCapabilityCodes.Manual),
            values.Contains(RegistrationProviderCapabilityCodes.SchemaRead),
            values.Contains(RegistrationProviderCapabilityCodes.FormProvision),
            values.Contains(RegistrationProviderCapabilityCodes.SubmissionWrite),
            values.Contains(RegistrationProviderCapabilityCodes.SubmissionRead),
            values.Contains(RegistrationProviderCapabilityCodes.CallbackVerification),
            values.Contains(RegistrationProviderCapabilityCodes.SubscriptionManagement),
            values.Contains(RegistrationProviderCapabilityCodes.Reconciliation),
            values.Contains(RegistrationProviderCapabilityCodes.SubmissionSink),
            values.Contains(RegistrationProviderCapabilityCodes.AutoFinalize));
    }

    public IEnumerable<string> ToCodes()
    {
        if (Redirect) yield return RegistrationProviderCapabilityCodes.Redirect;
        if (Embed) yield return RegistrationProviderCapabilityCodes.Embed;
        if (Manual) yield return RegistrationProviderCapabilityCodes.Manual;
        if (SchemaRead) yield return RegistrationProviderCapabilityCodes.SchemaRead;
        if (FormProvision) yield return RegistrationProviderCapabilityCodes.FormProvision;
        if (SubmissionWrite) yield return RegistrationProviderCapabilityCodes.SubmissionWrite;
        if (SubmissionRead) yield return RegistrationProviderCapabilityCodes.SubmissionRead;
        if (CallbackVerification) yield return RegistrationProviderCapabilityCodes.CallbackVerification;
        if (SubscriptionManagement) yield return RegistrationProviderCapabilityCodes.SubscriptionManagement;
        if (Reconciliation) yield return RegistrationProviderCapabilityCodes.Reconciliation;
        if (SubmissionSink) yield return RegistrationProviderCapabilityCodes.SubmissionSink;
        if (AutoFinalize) yield return RegistrationProviderCapabilityCodes.AutoFinalize;
    }
}

public static class RegistrationProviderCapabilityCodes
{
    public const string Redirect = "REDIRECT";
    public const string Embed = "EMBED";
    public const string Manual = "MANUAL";
    public const string SchemaRead = "SCHEMA_READ";
    public const string FormProvision = "FORM_PROVISION";
    public const string SubmissionWrite = "SUBMISSION_WRITE";
    public const string SubmissionRead = "SUBMISSION_READ";
    public const string CallbackVerification = "CALLBACK_VERIFICATION";
    public const string SubscriptionManagement = "SUBSCRIPTION_MANAGEMENT";
    public const string Reconciliation = "RECONCILIATION";
    public const string SubmissionSink = "SUBMISSION_SINK";
    public const string AutoFinalize = "AUTO_FINALIZE";
}

public interface IRegistrationProviderDescriptor
{
    RegistrationProviderTuple Tuple { get; }
    RegistrationProviderCapabilitySet ProvenCapabilities { get; }
}

public interface IRegistrationProviderPresentation
{
    Task<RegistrationProviderPresentationResult> GetPresentationAsync(RegistrationProviderPresentationRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderSchemaReader
{
    Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(RegistrationProviderSchemaReadRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderFormProvisioner
{
    Task<RegistrationProviderFormProvisionResult> ProvisionFormAsync(RegistrationProviderFormProvisionRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderFormCompatibilityChecker
{
    RegistrationProviderFormCompatibilityResult CheckCompatibility(RegistrationFormVersion formVersion);
}

public interface IRegistrationProviderSubmissionWriter
{
    Task<RegistrationProviderSubmissionWriteResult> WriteSubmissionAsync(RegistrationProviderSubmissionWriteRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubmissionReader
{
    Task<RegistrationProviderSubmissionReadResult> ReadSubmissionAsync(RegistrationProviderSubmissionReadRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderCallbackVerifier
{
    Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(RegistrationProviderCallbackVerificationRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderDelegatedAutomation
{
    string ConnectorContractVersion { get; }
    string RequiredCorrelationPlatformFieldKey { get; }
}

public interface IRegistrationProviderCallbackReceiptProtector
{
    string Protect(RegistrationProviderCallbackReceipt receipt);

    RegistrationProviderCallbackReceipt Unprotect(string protectedReceipt);
}

public interface IRegistrationProviderCallbackBindingResolver
{
    Task<RegistrationProviderBinding?> ResolveForCallbackAsync(
        string provider,
        Guid bindingId,
        CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubscriptionManager
{
    Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(RegistrationProviderSubscriptionRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderReconciliationProvider
{
    Task<RegistrationProviderReconciliationResult> ReconcileAsync(RegistrationProviderReconciliationRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubmissionSink
{
    Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(RegistrationProviderSubmissionSinkRequest request, CancellationToken cancellationToken);
}

public interface IRegistrationProviderRegistry
{
    IRegistrationProviderDescriptor? TryResolve(RegistrationProviderTuple tuple);
}

public interface IRegistrationProviderCallbackUriBuilder
{
    Uri Build(string providerCode, Guid bindingId);
}

public interface IRegistrationProviderManagedPublishPreflight
{
    Task<RegistrationProviderManagedPublishPreflightResult> RunAsync(
        Guid tenantId,
        Guid eventId,
        RegistrationProviderBinding binding,
        CancellationToken cancellationToken);
}

public interface IRegistrationProviderConnectionCheckpoint
{
    Task RecordCredentialRefreshAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken);

    Task RecordAccessValidatedAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken);
}

public sealed record RegistrationProviderPresentationRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, Guid? AttemptId = null, string? AttemptCapabilityToken = null);
public sealed record RegistrationProviderPresentationResult(bool RedirectAvailable, bool EmbedAvailable, bool ManualAvailable, Uri? RedirectUri = null, Uri? EmbedUri = null);
public sealed record RegistrationProviderSchemaReadRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple);
public sealed record RegistrationProviderSchemaReadResult(RegistrationProviderSchemaSnapshot Snapshot, bool IsActive = true, string? Fingerprint = null);
public sealed record RegistrationProviderFormProvisionRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, RegistrationFormVersion FormVersion);
public sealed record RegistrationProviderFormProvisionResult(string ProviderFormId, string ProviderRevisionId);
public sealed record RegistrationProviderFormCompatibilityResult(string Fingerprint, IReadOnlyList<RegistrationProviderPreflightIssue> Issues)
{
    public bool IsCompatible => Issues.Count == 0;
}
public sealed record RegistrationProviderPreflightIssue(string Code, string Message, Guid? FieldId = null);
public sealed record RegistrationProviderManagedPublishPreflightResult(bool Succeeded, string? FailureCode, IReadOnlyList<string> Errors)
{
    public static RegistrationProviderManagedPublishPreflightResult Success() => new(true, null, []);
    public static RegistrationProviderManagedPublishPreflightResult Failure(string code, IReadOnlyList<string>? errors = null) => new(false, code, errors ?? [code]);
}
public sealed record RegistrationProviderSubmissionWriteRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, Guid AttemptId, IReadOnlyDictionary<string, string> Answers);
public sealed record RegistrationProviderSubmissionWriteResult(string ProviderSubmissionId, string ProviderRevisionId);
public sealed class RegistrationProviderSubmissionDeliveryException(
    RegistrationProviderSubmissionDeliveryFailureKind failureKind,
    string failureCode,
    string? message = null,
    Exception? innerException = null) : Exception(message ?? failureCode, innerException)
{
    public RegistrationProviderSubmissionDeliveryFailureKind FailureKind { get; } = failureKind;
    public string FailureCode { get; } = failureCode;
}
public sealed class RegistrationProviderUnsupportedSubmissionException(
    string failureCode,
    string? message = null,
    Exception? innerException = null) : Exception(message ?? failureCode, innerException)
{
    public string FailureCode { get; } = failureCode;
}
public enum RegistrationProviderSubmissionDeliveryFailureKind
{
    RetryableBeforeHandoff = 1,
    PermanentBeforeHandoff = 2,
    AmbiguousAfterHandoff = 3
}
public sealed record RegistrationProviderSubmissionReadRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, string ProviderSubmissionId);
public sealed record RegistrationProviderSubmissionReadResult(string ProviderSubmissionId, string ProviderRevisionId, DateTime? ReceivedAt, Guid? AttemptId, IReadOnlyDictionary<string, JsonElement> Answers, string? AttemptCapabilityToken = null);
public sealed record RegistrationProviderCallbackVerificationRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Headers);
public sealed record RegistrationProviderCallbackVerificationResult(bool IsVerified, string? FailureCode = null, string? Receipt = null, string? ProviderSubmissionId = null, string? EffectKind = null);
public sealed record RegistrationProviderCallbackReceipt(
    Guid TenantId,
    Guid ConnectionId,
    Guid BindingId,
    string Provider,
    string TupleKey,
    string BodySha256,
    string ProviderSubmissionId,
    DateTimeOffset VerifiedAt,
    string Nonce);
public sealed record RegistrationProviderSubscriptionRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, Uri CallbackUri);
public sealed record RegistrationProviderSubscriptionResult(
    bool IsActive,
    string? ProviderSubscriptionId,
    bool ExternalSecretProvisioningRequired = false,
    DateTime? ExpiresAtUtc = null);
public sealed record RegistrationProviderReconciliationRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, DateTime SinceUtc, string? ContinuationCursor = null);
public sealed record RegistrationProviderReconciledSubmission(
    string ProviderSubmissionId,
    string ProviderRevisionId,
    DateTime? ReceivedAt);
public sealed record RegistrationProviderReconciliationResult(
    int ObservedSubmissionCount,
    bool HasMore,
    IReadOnlyList<RegistrationProviderReconciledSubmission>? Responses = null,
    string? NextCheckpoint = null,
    string? ContinuationCursor = null);
public sealed record RegistrationProviderSubmissionSinkRequest(Guid TenantId, RegistrationProviderBinding Binding, RegistrationProviderConnection Connection, RegistrationProviderTuple Tuple, Guid AttemptId, Guid RegistrationSubmissionId, IReadOnlyDictionary<string, string> Answers, string? ProviderSubmissionId);
public sealed record RegistrationProviderSubmissionSinkResult(bool Accepted, Guid SubmissionId, bool AutoFinalizable);

public sealed record RegistrationProviderSchemaSnapshot(IReadOnlyList<RegistrationProviderSchemaFieldSnapshot> Fields);

public sealed record RegistrationProviderSchemaFieldSnapshot(
    string Key,
    string Label,
    string Type,
    bool IsRequired,
    IReadOnlyList<RegistrationProviderSchemaOptionSnapshot> Options);

public sealed record RegistrationProviderSchemaOptionSnapshot(string Key, string Label);
