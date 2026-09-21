using System.Collections.Immutable;

namespace Explore.Application.DTOs.Onboarding;

public sealed record InstanceOperatorIdentityCapabilityReadinessDto(
    bool IsReady,
    string? FailureCode,
    ImmutableArray<string> ReasonCodes);

/// <summary>
/// Public document and readiness assessment representing the instance operator identity.
/// </summary>
public sealed record InstanceOperatorIdentityDocumentDto
{
    public required InstanceOperatorIdentityCapabilityReadinessDto PublicDisclosure { get; init; }
    public required InstanceOperatorIdentityCapabilityReadinessDto PaidCommerce { get; init; }
    public Guid? Revision { get; init; }
    public Guid? OperatorId { get; init; }
    public string? PublicName { get; init; }
    public string? LegalName { get; init; }
    public string? OperatorKindCode { get; init; }
    public string? JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public string? PublicContactEmail { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? LegalNoticeUrl { get; init; }
    public string? TermsUrl { get; init; }
    public string? PrivacyUrl { get; init; }
    public bool IsOfficialInstance { get; init; }
    public string? OfficialOrigin { get; init; }
}

/// <summary>
/// Candidate payload sent to update the instance operator identity.
/// </summary>
public sealed record SaveInstanceOperatorIdentityRequestDto
{
    public Guid? ExpectedRevision { get; init; }
    public string? PublicName { get; init; }
    public string? LegalName { get; init; }
    public string? OperatorKindCode { get; init; }
    public string? JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public string? PublicContactEmail { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? LegalNoticeUrl { get; init; }
    public string? TermsUrl { get; init; }
    public string? PrivacyUrl { get; init; }
    public string? OfficialOrigin { get; init; }
}

/// <summary>
/// Result of saving instance operator identity.
/// </summary>
public sealed record InstanceOperatorIdentitySavedDocumentDto
{
    public Guid Revision { get; init; }
    public required InstanceOperatorIdentityCapabilityReadinessDto PublicDisclosure { get; init; }
    public required InstanceOperatorIdentityCapabilityReadinessDto PaidCommerce { get; init; }
    public Guid? OperatorId { get; init; }
}
