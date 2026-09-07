namespace Explore.Application.DTOs.TenantSettingsDocuments;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;

internal static class TenantDirectoryOperatorIdentityDocumentMapper
{
    public static TenantDirectoryOperatorIdentityDocumentDto Map(
        ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings> document)
        => Map(
            document.DocumentKey,
            document.SchemaVersion,
            document.DefaultsVersion,
            document.Payload,
            document.Source,
            document.SourceScopeId,
            document.ConcurrencyStamp,
            document.UpdatedAt);

    public static TenantDirectoryOperatorIdentityDocumentDto Map(
        TenantSettingsDocument document,
        TenantDirectoryOperatorIdentitySettings payload)
        => Map(
            document.DocumentKey,
            document.SchemaVersion,
            document.DefaultsVersion,
            payload,
            SettingsDocumentSource.Tenant,
            document.TenantId,
            document.ConcurrencyStamp,
            document.UpdatedAt);

    private static TenantDirectoryOperatorIdentityDocumentDto Map(
        string documentKey,
        int schemaVersion,
        string defaultsVersion,
        TenantDirectoryOperatorIdentitySettings payload,
        SettingsDocumentSource source,
        Guid sourceScopeId,
        Guid concurrencyStamp,
        DateTime? updatedAt)
    {
        TenantDirectoryOperatorIdentityReadiness activation =
            TenantDirectoryOperatorIdentity.Evaluate(
                payload,
                TenantDirectoryOperatorIdentityCapability.Activation);
        TenantDirectoryOperatorIdentityReadiness publicDisclosure =
            TenantDirectoryOperatorIdentity.Evaluate(
                payload,
                TenantDirectoryOperatorIdentityCapability.PublicDisclosure);
        TenantDirectoryOperatorIdentityReadiness paidCommerce =
            TenantDirectoryOperatorIdentity.Evaluate(
                payload,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce);

        return new TenantDirectoryOperatorIdentityDocumentDto
        {
            DocumentKey = documentKey,
            SchemaVersion = schemaVersion,
            DefaultsVersion = defaultsVersion,
            Payload = new TenantDirectoryOperatorIdentityPayloadDto
            {
                PublicName = payload.PublicName,
                LegalName = payload.LegalName,
                OperatorKindCode = payload.OperatorKindCode,
                JurisdictionCountryCode = payload.JurisdictionCountryCode,
                RegistrationIdentifier = payload.RegistrationIdentifier,
                PublicContactEmail = payload.PublicContactEmail,
                LegalNoticeUrl = payload.LegalNoticeUrl,
                TermsUrl = payload.TermsUrl,
                PrivacyUrl = payload.PrivacyUrl
            },
            Source = source.ToString(),
            SourceScopeId = sourceScopeId,
            ConcurrencyStamp = concurrencyStamp,
            IsActivationReady = activation.IsReady,
            IsPublicDisclosureReady = publicDisclosure.IsReady,
            IsPaidCommerceReady = paidCommerce.IsReady,
            ActivationReasonCodes = activation.ReasonCodes,
            PublicDisclosureReasonCodes = publicDisclosure.ReasonCodes,
            PaidCommerceReasonCodes = paidCommerce.ReasonCodes,
            CanEdit = source == SettingsDocumentSource.Tenant,
            UpdatedAt = updatedAt
        };
    }
}
