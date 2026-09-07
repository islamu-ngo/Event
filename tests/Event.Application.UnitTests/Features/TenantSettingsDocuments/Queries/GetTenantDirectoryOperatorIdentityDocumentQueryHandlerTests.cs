namespace Event.Application.UnitTests.Features.TenantSettingsDocuments.Queries;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Queries;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using NSubstitute;

public sealed class GetTenantDirectoryOperatorIdentityDocumentQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ITypedSettingsDocumentResolver _resolver =
        Substitute.For<ITypedSettingsDocumentResolver>();

    [Test]
    public async Task Handle_MapsTenantDocumentAndCapabilityReadiness()
    {
        Guid revision = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(_tenantId);
        _resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns(new ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>
            {
                DocumentKey = SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                SchemaVersion = 1,
                DefaultsVersion = "2026-08-28",
                Payload = new TenantDirectoryOperatorIdentitySettings
                {
                    PublicName = "Community Events",
                    LegalName = "Community Events ASBL",
                    OperatorKindCode = "registered_organization",
                    JurisdictionCountryCode = "BE",
                    PublicContactEmail = "contact@example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    TermsUrl = null,
                    PrivacyUrl = "https://example.test/privacy"
                },
                Source = SettingsDocumentSource.Tenant,
                SourceScopeId = _tenantId,
                ConcurrencyStamp = revision
            });
        var handler = new GetTenantDirectoryOperatorIdentityDocumentQueryHandler(
            _tenantContext,
            _resolver);

        var result = await handler.Handle(
            new GetTenantDirectoryOperatorIdentityDocumentQuery(_tenantId),
            CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ConcurrencyStamp).IsEqualTo(revision);
        await Assert.That(result.Payload.PublicName).IsEqualTo("Community Events");
        await Assert.That(result.IsActivationReady).IsTrue();
        await Assert.That(result.IsPublicDisclosureReady).IsTrue();
        await Assert.That(result.IsPaidCommerceReady).IsFalse();
        await Assert.That(result.PaidCommerceReasonCodes)
            .Contains("tenant_directory_operator_identity_terms_url_missing");
    }

    [Test]
    public async Task Handle_MissingDocumentReturnsNullWithoutProvisioning()
    {
        _tenantContext.TenantId.Returns(_tenantId);
        _resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>?)null);
        var handler = new GetTenantDirectoryOperatorIdentityDocumentQueryHandler(
            _tenantContext,
            _resolver);

        var result = await handler.Handle(
            new GetTenantDirectoryOperatorIdentityDocumentQuery(_tenantId),
            CancellationToken.None);

        await Assert.That(result).IsNull();
    }
}
