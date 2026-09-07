namespace Explore.Application.Features.LegalDocuments.Handlers.Queries;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.LegalDocuments;
using Explore.Application.Features.ConfigurationManifest.LegalDocuments;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using MediatR;

public sealed class GetPublicLegalDocumentQueryHandler(
    ILegalDocumentRepository repository,
    LegalDocumentRenderingService renderingService,
    ITenantContext tenantContext,
    ITenantDirectoryOperatorReadinessEvaluator tenantIdentityReadiness,
    IInstanceOperatorIdentity instanceIdentity)
    : IRequestHandler<
        GetPublicLegalDocumentQuery,
        PublicLegalDocumentQueryResult>
{
    public async Task<PublicLegalDocumentQueryResult> Handle(
        GetPublicLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!LegalDocumentKindCatalog.TryGet(
                request.KindCode,
                out LegalDocumentKindDescriptor? descriptor)
            || descriptor is null)
        {
            return PublicLegalDocumentQueryResult.NotFound();
        }

        Guid? tenantId = descriptor.Scope == LegalDocumentScope.Tenant
            ? tenantContext.TenantId
            : null;
        LegalDocument? document = await repository.GetPublishedAsync(
            descriptor.Scope,
            tenantId,
            descriptor.Kind,
            cancellationToken);
        if (document is null)
            return PublicLegalDocumentQueryResult.NotFound();

        IReadOnlyDictionary<string, string>? identities =
            descriptor.Scope == LegalDocumentScope.Instance
                ? InstanceIdentityValues(instanceIdentity)
                : await TenantIdentityValuesAsync(
                    tenantId!.Value,
                    cancellationToken);
        if (identities is null)
            return PublicLegalDocumentQueryResult.Unavailable();

        LegalDocumentRenderView view;
        try
        {
            view = renderingService.RenderLastPublished(
                document,
                request.LanguageTag,
                identities);
        }
        catch (ArgumentException)
        {
            return PublicLegalDocumentQueryResult.Unavailable();
        }

        if (!view.IsReady)
        {
            bool absent = view.Diagnostics.Any(diagnostic =>
                diagnostic.Code is
                    LegalDocumentRenderDiagnosticCodes.NotPublished
                    or LegalDocumentRenderDiagnosticCodes.NotPublic);
            return absent
                ? PublicLegalDocumentQueryResult.NotFound()
                : PublicLegalDocumentQueryResult.Unavailable();
        }

        return PublicLegalDocumentQueryResult.Available(
            new PublicLegalDocumentDto
            {
                KindCode = descriptor.Code,
                ScopeCode = ScopeCode(descriptor.Scope),
                OwnerRoleCode = OwnerRoleCode(descriptor.OwnerRole),
                Title = view.Title,
                Summary = view.Summary,
                LanguageTag = view.LanguageTag,
                RenderedHtml = view.Html,
                Version = view.Version!.Value,
                EffectiveAt = view.EffectiveAt!.Value,
                ContentDigest = view.ContentDigest!,
                IsLocaleFallback = view.Diagnostics.Any(diagnostic =>
                    diagnostic.Code
                        == LegalDocumentRenderDiagnosticCodes.LocaleFallback)
            });
    }

    private async Task<IReadOnlyDictionary<string, string>?>
        TenantIdentityValuesAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
    {
        TenantDirectoryOperatorReadinessAssessment assessment =
            await tenantIdentityReadiness.EvaluateAsync(
                tenantId,
                TenantDirectoryOperatorIdentityCapability.PublicDisclosure,
                cancellationToken);
        return assessment.IsReady && assessment.Identity is not null
            ? TenantIdentityValues(assessment.Identity)
            : null;
    }

    private static IReadOnlyDictionary<string, string> InstanceIdentityValues(
        IInstanceOperatorIdentity identity)
    {
        var values = CommonIdentityValues(
            identity.PublicName,
            identity.LegalName,
            identity.OperatorKindCode,
            identity.JurisdictionCountryCode,
            identity.RegistrationIdentifier,
            identity.PublicContactEmail,
            identity.LegalNoticeUrl,
            identity.TermsUrl,
            identity.PrivacyUrl);
        values["operator.website_url"] = identity.WebsiteUrl;
        return values;
    }

    private static IReadOnlyDictionary<string, string> TenantIdentityValues(
        TenantDirectoryOperatorIdentity identity) =>
        CommonIdentityValues(
            identity.PublicName,
            identity.LegalName,
            identity.OperatorKindCode,
            identity.JurisdictionCountryCode,
            identity.RegistrationIdentifier,
            identity.PublicContactEmail,
            identity.LegalNoticeUrl,
            identity.TermsUrl,
            identity.PrivacyUrl);

    private static Dictionary<string, string> CommonIdentityValues(
        string publicName,
        string legalName,
        string operatorKind,
        string jurisdictionCountry,
        string? registrationIdentifier,
        string publicContactEmail,
        string legalNoticeUrl,
        string? termsUrl,
        string privacyUrl)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accountable_identity"] = legalName,
            ["operator.public_name"] = publicName,
            ["operator.legal_name"] = legalName,
            ["operator.kind"] = operatorKind,
            ["operator.jurisdiction_country"] = jurisdictionCountry,
            ["operator.public_contact_email"] = publicContactEmail,
            ["operator.legal_notice_url"] = legalNoticeUrl,
            ["operator.privacy_url"] = privacyUrl
        };
        if (!string.IsNullOrWhiteSpace(registrationIdentifier))
            values["operator.registration_identifier"] = registrationIdentifier;
        if (!string.IsNullOrWhiteSpace(termsUrl))
            values["operator.terms_url"] = termsUrl;
        return values;
    }

    private static string ScopeCode(LegalDocumentScope scope) => scope switch
    {
        LegalDocumentScope.Instance => "instance",
        LegalDocumentScope.Tenant => "tenant",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static string OwnerRoleCode(LegalDocumentOwnerRole role) => role switch
    {
        LegalDocumentOwnerRole.InstanceOperator => "instance_operator",
        LegalDocumentOwnerRole.TenantOperator => "tenant_operator",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
