using System.Collections.Immutable;
using Explore.Domain.Settings.Documents.Payloads;

namespace Explore.Domain.ValueObjects;

/// <summary>
/// Instance-scoped reason codes for operator identity validation. Legal-entity field
/// codes are mapped from <see cref="TenantDirectoryOperatorIdentityReasonCodes"/> by
/// swapping the tenant directory prefix for the instance operator prefix.
/// </summary>
public static class InstanceOperatorIdentityReasonCodes
{
    public const string IncompleteFailureCode = "instance_operator_identity_incomplete";

    public const string OperatorIdInvalid = "instance_operator_identity_operator_id_invalid";

    public const string OfficialOriginMissing = "instance_operator_identity_official_origin_missing";

    public const string OfficialOriginInvalid = "instance_operator_identity_official_origin_invalid";

    public const string WebsiteUrlMissing = "instance_operator_identity_website_url_missing";

    public const string WebsiteUrlInvalid = "instance_operator_identity_website_url_invalid";
}

/// <summary>
/// Readiness evaluation for the persisted instance operator identity document.
/// </summary>
/// <param name="IsReady">True when every required field is present and valid.</param>
/// <param name="FailureCode">
/// <see cref="InstanceOperatorIdentityReasonCodes.IncompleteFailureCode"/> when the
/// document is not ready; null when ready. Missing-document and corrupt-JSON failures
/// are classified by the application-level readiness evaluator.
/// </param>
/// <param name="ReasonCodes">Bounded, stable reason codes describing incomplete fields.</param>
/// <param name="Normalized">Fully normalized payload when ready; null otherwise.</param>
public sealed record InstanceOperatorIdentityReadiness(
    bool IsReady,
    string? FailureCode,
    ImmutableArray<string> ReasonCodes,
    InstanceOperatorIdentitySettings? Normalized)
{
    private const string TenantReasonPrefix = "tenant_directory_operator_identity_";
    private const string InstanceReasonPrefix = "instance_operator_identity_";

    /// <summary>
    /// Evaluates the persisted identity document for the requested capability:
    /// required legal-entity fields, a UUIDv7 operator id, a normalized
    /// HTTPS origin, and an HTTPS website URL.
    /// </summary>
    public static InstanceOperatorIdentityReadiness Evaluate(
        InstanceOperatorIdentitySettings settings,
        InstanceOperatorIdentityCapability capability)
    {
        ArgumentNullException.ThrowIfNull(settings);

        TenantDirectoryOperatorIdentityCapability legalCapability = capability switch
        {
            InstanceOperatorIdentityCapability.PublicDisclosure => TenantDirectoryOperatorIdentityCapability.PublicDisclosure,
            InstanceOperatorIdentityCapability.PaidCommerce => TenantDirectoryOperatorIdentityCapability.PaidCommerce,
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unsupported identity capability.")
        };
        var reasons = ImmutableArray.CreateBuilder<string>();
        TenantDirectoryOperatorIdentityReadiness legal = TenantDirectoryOperatorIdentity.Evaluate(
            ToLegalSettings(settings), legalCapability);
        reasons.AddRange(legal.ReasonCodes.Select(MapReasonCode));

        if (settings.OperatorId is not { } operatorId || operatorId == Guid.Empty || operatorId.Version != 7)
        {
            reasons.Add(InstanceOperatorIdentityReasonCodes.OperatorIdInvalid);
        }

        string? officialOrigin = NormalizeOfficialOrigin(settings.OfficialOrigin);
        if (officialOrigin is null)
        {
            reasons.Add(string.IsNullOrWhiteSpace(settings.OfficialOrigin)
                ? InstanceOperatorIdentityReasonCodes.OfficialOriginMissing
                : InstanceOperatorIdentityReasonCodes.OfficialOriginInvalid);
        }

        string? websiteUrl = NormalizeHttpsUrl(settings.WebsiteUrl);
        if (websiteUrl is null)
        {
            reasons.Add(string.IsNullOrWhiteSpace(settings.WebsiteUrl)
                ? InstanceOperatorIdentityReasonCodes.WebsiteUrlMissing
                : InstanceOperatorIdentityReasonCodes.WebsiteUrlInvalid);
        }

        if (reasons.Count > 0)
        {
            return new(
                false,
                InstanceOperatorIdentityReasonCodes.IncompleteFailureCode,
                reasons.ToImmutable(),
                null);
        }

        TenantDirectoryOperatorIdentity legalIdentity = legal.Identity!;
        return new(
            true,
            null,
            [],
            settings with
            {
                PublicName = legalIdentity.PublicName,
                LegalName = legalIdentity.LegalName,
                OperatorKindCode = legalIdentity.OperatorKindCode,
                JurisdictionCountryCode = legalIdentity.JurisdictionCountryCode,
                RegistrationIdentifier = legalIdentity.RegistrationIdentifier,
                PublicContactEmail = legalIdentity.PublicContactEmail,
                LegalNoticeUrl = legalIdentity.LegalNoticeUrl,
                TermsUrl = legalIdentity.TermsUrl,
                PrivacyUrl = legalIdentity.PrivacyUrl,
                OfficialOrigin = officialOrigin,
                WebsiteUrl = websiteUrl
            });
    }

    /// <summary>
    /// Normalizes a save candidate without requiring completeness: missing fields are
    /// allowed so onboarding drafts can be persisted, but malformed values are rejected.
    /// </summary>
    public static InstanceOperatorIdentityDraftValidation ValidateDraft(InstanceOperatorIdentitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reasons = ImmutableArray.CreateBuilder<string>();
        TenantDirectoryOperatorIdentityDraftValidation legal =
            TenantDirectoryOperatorIdentity.ValidateDraft(ToLegalSettings(settings));
        reasons.AddRange(legal.ReasonCodes.Select(MapReasonCode));

        Guid? operatorId = settings.OperatorId;
        if (operatorId is not null && (operatorId == Guid.Empty || operatorId.Value.Version != 7))
        {
            reasons.Add(InstanceOperatorIdentityReasonCodes.OperatorIdInvalid);
            operatorId = null;
        }

        string? officialOrigin = NormalizeOfficialOrigin(settings.OfficialOrigin);
        if (officialOrigin is null && !string.IsNullOrWhiteSpace(settings.OfficialOrigin))
        {
            reasons.Add(InstanceOperatorIdentityReasonCodes.OfficialOriginInvalid);
        }

        string? websiteUrl = NormalizeHttpsUrl(settings.WebsiteUrl);
        if (websiteUrl is null && !string.IsNullOrWhiteSpace(settings.WebsiteUrl))
        {
            reasons.Add(InstanceOperatorIdentityReasonCodes.WebsiteUrlInvalid);
        }

        return new(
            new InstanceOperatorIdentitySettings
            {
                OperatorId = operatorId,
                PublicName = legal.NormalizedSettings.PublicName,
                LegalName = legal.NormalizedSettings.LegalName,
                OperatorKindCode = legal.NormalizedSettings.OperatorKindCode,
                JurisdictionCountryCode = legal.NormalizedSettings.JurisdictionCountryCode,
                RegistrationIdentifier = legal.NormalizedSettings.RegistrationIdentifier,
                PublicContactEmail = legal.NormalizedSettings.PublicContactEmail,
                LegalNoticeUrl = legal.NormalizedSettings.LegalNoticeUrl,
                TermsUrl = legal.NormalizedSettings.TermsUrl,
                PrivacyUrl = legal.NormalizedSettings.PrivacyUrl,
                IsOfficialInstance = settings.IsOfficialInstance,
                OfficialOrigin = officialOrigin,
                WebsiteUrl = websiteUrl,
                Revision = settings.Revision
            },
            reasons.ToImmutable());
    }

    private static string MapReasonCode(string reasonCode) =>
        reasonCode.StartsWith(TenantReasonPrefix, StringComparison.Ordinal)
            ? string.Concat(InstanceReasonPrefix, reasonCode.AsSpan(TenantReasonPrefix.Length))
            : reasonCode;

    private static TenantDirectoryOperatorIdentitySettings ToLegalSettings(
        InstanceOperatorIdentitySettings settings) => new()
        {
            PublicName = settings.PublicName,
            LegalName = settings.LegalName,
            OperatorKindCode = settings.OperatorKindCode,
            JurisdictionCountryCode = settings.JurisdictionCountryCode,
            RegistrationIdentifier = settings.RegistrationIdentifier,
            PublicContactEmail = settings.PublicContactEmail,
            LegalNoticeUrl = settings.LegalNoticeUrl,
            TermsUrl = settings.TermsUrl,
            PrivacyUrl = settings.PrivacyUrl
        };

    private static string? NormalizeOfficialOrigin(string? value)
    {
        string? normalized = NormalizeHttpsUrl(value);
        if (normalized is null
            || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? NormalizeHttpsUrl(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : ExternalActionUrl.Create(value).Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// Draft validation outcome: normalized candidate plus reason codes for malformed values.
/// </summary>
public sealed record InstanceOperatorIdentityDraftValidation(
    InstanceOperatorIdentitySettings Normalized,
    ImmutableArray<string> ReasonCodes)
{
    public bool IsValid => ReasonCodes.IsEmpty;
}
