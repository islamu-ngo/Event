namespace Explore.Application.Contracts.Services;

using System.Collections.Immutable;
using Explore.Domain.ValueObjects;

public sealed record TenantDirectoryOperatorReadinessAssessment(
    bool IsReady,
    string? FailureCode,
    ImmutableArray<string> ReasonCodes,
    TenantDirectoryOperatorIdentity? Identity,
    Guid? DocumentId,
    Guid? DocumentRevision)
{
    public static TenantDirectoryOperatorReadinessAssessment Missing { get; } =
        new(
            false,
            "tenant_directory_operator_identity_missing",
            ["tenant_directory_operator_identity_missing"],
            null,
            null,
            null);

    public static TenantDirectoryOperatorReadinessAssessment Incomplete(
        IEnumerable<string> reasonCodes,
        Guid documentRevision) =>
        new(
            false,
            "tenant_directory_operator_identity_incomplete",
            [.. reasonCodes],
            null,
            null,
            documentRevision);

    public static TenantDirectoryOperatorReadinessAssessment IntegrityError { get; } =
        new(
            false,
            "tenant_directory_operator_identity_integrity_error",
            ["tenant_directory_operator_identity_integrity_error"],
            null,
            null,
            null);

    public static TenantDirectoryOperatorReadinessAssessment Ready(
        TenantDirectoryOperatorIdentity identity,
        Guid documentRevision,
        Guid documentId) =>
        new(true, null, [], identity, documentId, documentRevision);
}

public interface ITenantDirectoryOperatorReadinessEvaluator
{
    Task<TenantDirectoryOperatorReadinessAssessment> EvaluateAsync(
        Guid tenantId,
        TenantDirectoryOperatorIdentityCapability capability,
        CancellationToken cancellationToken = default);
}

public static class TenantDirectoryOperatorReadinessReasonCodePolicy
{
    private static readonly IReadOnlySet<string> ClosedCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "tenant_directory_operator_identity_missing",
        "tenant_directory_operator_identity_integrity_error",
        TenantDirectoryOperatorIdentityReasonCodes.MissingPublicName,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicName,
        TenantDirectoryOperatorIdentityReasonCodes.MissingLegalName,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalName,
        TenantDirectoryOperatorIdentityReasonCodes.MissingOperatorKind,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidOperatorKind,
        TenantDirectoryOperatorIdentityReasonCodes.MissingJurisdictionCountry,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidJurisdictionCountry,
        TenantDirectoryOperatorIdentityReasonCodes.MissingPublicContactEmail,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicContactEmail,
        TenantDirectoryOperatorIdentityReasonCodes.MissingLegalNoticeUrl,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalNoticeUrl,
        TenantDirectoryOperatorIdentityReasonCodes.MissingTermsUrl,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidTermsUrl,
        TenantDirectoryOperatorIdentityReasonCodes.MissingPrivacyUrl,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidPrivacyUrl,
        TenantDirectoryOperatorIdentityReasonCodes.InvalidRegistrationIdentifier
    };

    public static bool IsClosedCode(string code) => ClosedCodes.Contains(code);
}
