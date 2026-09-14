namespace Explore.Domain.Settings.Documents.Payloads;

/// <summary>
/// Persisted instance operator identity document stored under the dedicated
/// <c>instance.operator_identity</c> system setting key. <see cref="OperatorId"/>,
/// <see cref="IsOfficialInstance"/>, and <see cref="Revision"/> are server-controlled
/// and never accepted from client payloads.
/// </summary>
public sealed record InstanceOperatorIdentitySettings
{
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

    public Guid? Revision { get; init; }
}
