using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class TenantDirectoryOperatorIdentityAdminServiceTests
{
    private readonly ITenantSettingsDocumentsClient _api = Substitute.For<ITenantSettingsDocumentsClient>();

    [Test]
    public async Task GetAsync_MapsAllIdentityAndReadinessGroups_FromExactEditAffordanceOnly()
    {
        Guid revision = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateDocument(revision, includeExactEdit: true));

        TenantDirectoryOperatorIdentityAdminModel model =
            await CreateService().GetAsync();

        await Assert.That(model.CanEdit).IsTrue();
        await Assert.That(model.ConcurrencyStamp).IsEqualTo(revision);
        await Assert.That(model.PublicName).IsEqualTo("Community Directory");
        await Assert.That(model.LegalName).IsEqualTo("Community Directory Foundation");
        await Assert.That(model.OperatorKindCode).IsEqualTo("NONPROFIT");
        await Assert.That(model.JurisdictionCountryCode).IsEqualTo("DE");
        await Assert.That(model.RegistrationIdentifier).IsEqualTo("VR 12345");
        await Assert.That(model.PublicContactEmail).IsEqualTo("support@directory.example");
        await Assert.That(model.LegalNoticeUrl).IsEqualTo("https://directory.example/legal");
        await Assert.That(model.TermsUrl).IsEqualTo("https://directory.example/terms");
        await Assert.That(model.PrivacyUrl).IsEqualTo("https://directory.example/privacy");
        await Assert.That(model.IsActivationReady).IsTrue();
        await Assert.That(model.IsPublicDisclosureReady).IsTrue();
        await Assert.That(model.IsPaidCommerceReady).IsTrue();
    }

    [Test]
    public async Task GetAsync_DoesNotInferEditAuthority_FromDtoFlagOrNearMatchLinks()
    {
        foreach (HalResourceOfTenantDirectoryOperatorIdentityDocumentDto document in
                 new[]
                 {
                     CreateDocument(Guid.NewGuid(), canEditFlag: true),
                     CreateDocument(Guid.NewGuid(), linkRel: "Edit", linkMethod: "PATCH"),
                     CreateDocument(Guid.NewGuid(), linkRel: "edit", linkMethod: "GET")
                 })
        {
            _api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(document);

            TenantDirectoryOperatorIdentityAdminModel model =
                await CreateService().GetAsync();

            await Assert.That(model.CanEdit).IsFalse();
        }
    }

    [Test]
    public async Task GetAsync_RejectsExternalWrongAndQueryEditHrefs()
    {
        foreach (string href in new[]
                 {
                     "https://evil.example/api/tenant/settings/documents/directory-operator-identity",
                     "/api/tenant/settings/documents/other",
                     "/api/tenant/settings/documents/directory-operator-identity?override=true"
                 })
        {
            HalResourceOfTenantDirectoryOperatorIdentityDocumentDto document =
                CreateDocument(Guid.NewGuid(), includeExactEdit: true);
            document._links!["edit"].Href = href;
            _api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(document);

            TenantDirectoryOperatorIdentityAdminModel model =
                await CreateService().GetAsync();

            await Assert.That(model.CanEdit).IsFalse();
        }
    }

    [Test]
    public async Task SaveAsync_SendsGroupedPatchWithCurrentRevision_AndChainsReturnedRevision()
    {
        Guid currentRevision = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid updatedRevision = Guid.Parse("33333333-3333-3333-3333-333333333333");
        PatchTenantDirectoryOperatorIdentityDocumentDto? observed = null;
        _api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateDocument(currentRevision, includeExactEdit: true));
        _api.PatchTenantDirectoryOperatorIdentityDocumentAsync(
                Arg.Do<PatchTenantDirectoryOperatorIdentityDocumentDto>(request => observed = request),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateDocument(
                updatedRevision,
                includeExactEdit: true,
                publicName: "Updated Directory"));
        TenantDirectoryOperatorIdentityAdminService service = CreateService();
        TenantDirectoryOperatorIdentityAdminModel model = await service.GetAsync();

        TenantDirectoryOperatorIdentitySaveResult result = await service.SaveAsync(model);

        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.ExpectedConcurrencyStamp).IsEqualTo(currentRevision);
        await Assert.That(observed.LegalEntity).IsNotNull();
        await Assert.That(observed.Contacts).IsNotNull();
        await Assert.That(observed.LegalLinks).IsNotNull();
        await Assert.That(observed.LegalEntity!.PublicName?.Value)
            .IsEqualTo("Community Directory");
        await Assert.That(observed.LegalEntity.LegalName?.Value)
            .IsEqualTo("Community Directory Foundation");
        await Assert.That(observed.LegalEntity.OperatorKindCode?.Value).IsEqualTo("NONPROFIT");
        await Assert.That(observed.LegalEntity.JurisdictionCountryCode?.Value).IsEqualTo("DE");
        await Assert.That(observed.LegalEntity.RegistrationIdentifier?.Value).IsEqualTo("VR 12345");
        await Assert.That(observed.Contacts!.PublicContactEmail?.Value)
            .IsEqualTo("support@directory.example");
        await Assert.That(observed.LegalLinks!.LegalNoticeUrl?.Value)
            .IsEqualTo("https://directory.example/legal");
        await Assert.That(observed.LegalLinks.TermsUrl?.Value)
            .IsEqualTo("https://directory.example/terms");
        await Assert.That(observed.LegalLinks.PrivacyUrl?.Value)
            .IsEqualTo("https://directory.example/privacy");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Model!.ConcurrencyStamp).IsEqualTo(updatedRevision);
        await Assert.That(result.Model.PublicName).IsEqualTo("Updated Directory");
    }

    [Test]
    public async Task SaveAsync_WhenPatchConflicts_ReloadsAndReturnsAuthoritativeState()
    {
        Guid initialRevision = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid authoritativeRevision = Guid.Parse("55555555-5555-5555-5555-555555555555");
        _api.GetTenantDirectoryOperatorIdentityDocumentAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                CreateDocument(initialRevision, includeExactEdit: true),
                CreateDocument(
                    authoritativeRevision,
                    includeExactEdit: true,
                    publicName: "Authoritative Directory"));
        _api.PatchTenantDirectoryOperatorIdentityDocumentAsync(
                Arg.Any<PatchTenantDirectoryOperatorIdentityDocumentDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantDirectoryOperatorIdentityDocumentDto>>(_ =>
                throw new ApiException(
                    "Conflict",
                    409,
                    string.Empty,
                    new Dictionary<string, IEnumerable<string>>(),
                    null));
        TenantDirectoryOperatorIdentityAdminService service = CreateService();
        TenantDirectoryOperatorIdentityAdminModel model = await service.GetAsync();

        TenantDirectoryOperatorIdentitySaveResult result = await service.SaveAsync(model);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.IsConcurrencyConflict).IsTrue();
        await Assert.That(result.Model!.ConcurrencyStamp).IsEqualTo(authoritativeRevision);
        await Assert.That(result.Model.PublicName).IsEqualTo("Authoritative Directory");
    }

    private TenantDirectoryOperatorIdentityAdminService CreateService() => new(
        _api,
        Substitute.For<ILogger<TenantDirectoryOperatorIdentityAdminService>>());

    private static HalResourceOfTenantDirectoryOperatorIdentityDocumentDto CreateDocument(
        Guid revision,
        bool includeExactEdit = false,
        bool canEditFlag = false,
        string? linkRel = null,
        string linkMethod = "PATCH",
        string publicName = "Community Directory")
    {
        var document = new HalResourceOfTenantDirectoryOperatorIdentityDocumentDto
        {
            ConcurrencyStamp = revision,
            CanEdit = canEditFlag,
            IsActivationReady = true,
            IsPublicDisclosureReady = true,
            IsPaidCommerceReady = true,
            Payload = new Payload2
            {
                PublicName = publicName,
                LegalName = "Community Directory Foundation",
                OperatorKindCode = "NONPROFIT",
                JurisdictionCountryCode = "DE",
                RegistrationIdentifier = "VR 12345",
                PublicContactEmail = "support@directory.example",
                LegalNoticeUrl = "https://directory.example/legal",
                TermsUrl = "https://directory.example/terms",
                PrivacyUrl = "https://directory.example/privacy"
            }
        };

        string? rel = includeExactEdit ? "edit" : linkRel;
        if (rel is not null)
        {
            document._links = new Dictionary<string, HalLink>
            {
                [rel] = new()
                {
                    Href = "/api/tenant/settings/documents/directory-operator-identity",
                    Method = linkMethod
                }
            };
        }

        return document;
    }
}
