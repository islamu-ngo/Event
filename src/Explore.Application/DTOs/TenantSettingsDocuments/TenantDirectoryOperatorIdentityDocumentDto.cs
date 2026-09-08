namespace Explore.Application.DTOs.TenantSettingsDocuments;

using System.Collections.Immutable;
using Explore.Application.Models.Common;

public sealed record TenantDirectoryOperatorIdentityDocumentDto
{
    public required string DocumentKey { get; init; }
    public required int SchemaVersion { get; init; }
    public required string DefaultsVersion { get; init; }
    public required TenantDirectoryOperatorIdentityPayloadDto Payload { get; init; }
    public required string Source { get; init; }
    public required Guid SourceScopeId { get; init; }
    public required Guid ConcurrencyStamp { get; init; }
    public bool IsActivationReady { get; init; }
    public bool IsPublicDisclosureReady { get; init; }
    public bool IsPaidCommerceReady { get; init; }
    public ImmutableArray<string> ActivationReasonCodes { get; init; } = [];
    public ImmutableArray<string> PublicDisclosureReasonCodes { get; init; } = [];
    public ImmutableArray<string> PaidCommerceReasonCodes { get; init; } = [];
    public bool CanEdit { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record TenantDirectoryOperatorIdentityPayloadDto
{
    public string? PublicName { get; init; }
    public string? LegalName { get; init; }
    public string? OperatorKindCode { get; init; }
    public string? JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public string? PublicContactEmail { get; init; }
    public string? LegalNoticeUrl { get; init; }
    public string? TermsUrl { get; init; }
    public string? PrivacyUrl { get; init; }
}

public sealed record PatchTenantDirectoryOperatorIdentityDocumentDto
{
    public required Guid ExpectedConcurrencyStamp { get; init; }
    public PatchTenantDirectoryOperatorLegalEntityDto? LegalEntity { get; init; }
    public PatchTenantDirectoryOperatorContactsDto? Contacts { get; init; }
    public PatchTenantDirectoryOperatorLegalLinksDto? LegalLinks { get; init; }
}

public sealed record PatchTenantDirectoryOperatorLegalEntityDto
{
    public OptionalUpdate<string?> PublicName { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> LegalName { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> OperatorKindCode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> JurisdictionCountryCode { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> RegistrationIdentifier { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record PatchTenantDirectoryOperatorContactsDto
{
    public OptionalUpdate<string?> PublicContactEmail { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record PatchTenantDirectoryOperatorLegalLinksDto
{
    public OptionalUpdate<string?> LegalNoticeUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> TermsUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> PrivacyUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
}
