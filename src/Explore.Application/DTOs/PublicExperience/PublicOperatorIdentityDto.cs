namespace Explore.Application.DTOs.PublicExperience;

public sealed record TenantDirectoryOperatorPublicDto
{
    public required Guid DocumentRevision { get; init; }
    public required string PublicName { get; init; }
    public required string LegalName { get; init; }
    public required string OperatorKindCode { get; init; }
    public required string JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public required string PublicContactEmail { get; init; }
    public required string LegalNoticeUrl { get; init; }
    public string? TermsUrl { get; init; }
    public required string PrivacyUrl { get; init; }
}

public sealed record InstanceOperatorPublicDto
{
    public required Guid OperatorId { get; init; }
    public required string PublicName { get; init; }
    public required string LegalName { get; init; }
    public required bool IsOfficialInstance { get; init; }
    public required string OfficialOrigin { get; init; }
    public required string OperatorKindCode { get; init; }
    public required string JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public required string PublicContactEmail { get; init; }
    public required string WebsiteUrl { get; init; }
    public required string LegalNoticeUrl { get; init; }
    public required string TermsUrl { get; init; }
    public required string PrivacyUrl { get; init; }
}
