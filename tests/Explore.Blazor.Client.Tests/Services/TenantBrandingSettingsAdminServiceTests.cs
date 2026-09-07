namespace Explore.Blazor.Client.Tests.Services;

public sealed class TenantBrandingSettingsAdminServiceTests
{
    private readonly ITenantSettingsDocumentsClient _api = Substitute.For<ITenantSettingsDocumentsClient>();
    private readonly TenantBrandingSettingsAdminService _service;

    public TenantBrandingSettingsAdminServiceTests()
    {
        _service = new TenantBrandingSettingsAdminService(
            _api,
            Substitute.For<ILogger<TenantBrandingSettingsAdminService>>());
    }

    [Test]
    public async Task GetAsync_WhenApiReturnsHalDocument_MapsPayloadAndEditAffordance()
    {
        var stamp = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _api.GetTenantBrandingSettingsDocumentAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateDocument(stamp, "Typed Tenant", "https://cdn.example.test/tenant.css"));

        var result = await _service.GetAsync();

        await Assert.That(result.Exists).IsTrue();
        await Assert.That(result.CanReplace).IsTrue();
        await Assert.That(result.CanChangeDisplayName).IsTrue();
        await Assert.That(result.CanChangeLogoUrl).IsTrue();
        await Assert.That(result.CanChangeFaviconUrl).IsTrue();
        await Assert.That(result.CanChangeCustomCssUrl).IsTrue();
        await Assert.That(result.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(result.DisplayName).IsEqualTo("Typed Tenant");
        await Assert.That(result.CustomCssUrl).IsEqualTo("https://cdn.example.test/tenant.css");
    }

    [Test]
    public async Task GetAsync_WhenApiReturnsNotFound_ReportsMissingDocument()
    {
        _api.GetTenantBrandingSettingsDocumentAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantBrandingSettingsDocumentDto>>(_ => throw new ApiException(
                "Not found",
                404,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var result = await _service.GetAsync();

        await Assert.That(result.Exists).IsFalse();
        await Assert.That(result.CanReplace).IsFalse();
        await Assert.That(result.ErrorMessage)
            .IsEqualTo("Tenant branding settings have not been initialized.");
    }

    [Test]
    public async Task PatchDisplayNameAsync_WhenHalActionIsMissing_DoesNotCallApi()
    {
        var model = new TenantBrandingSettingsAdminModel
        {
            Exists = true,
            CanReplace = false,
            CanChangeDisplayName = true,
            ConcurrencyStamp = Guid.NewGuid(),
            DisplayName = "No Access"
        };

        var result = await _service.PatchDisplayNameAsync(model);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message)
            .IsEqualTo("The API did not expose a tenant branding edit affordance.");
        await _api.DidNotReceive().PatchTenantBrandingSettingsDocumentAsync(
            Arg.Any<PatchTenantBrandingSettingsDocumentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchDisplayNameAsync_WhenApiSucceeds_SendsOnlyDisplayNameWithCurrentStamp()
    {
        var expectedStamp = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var updatedStamp = Guid.Parse("33333333-3333-3333-3333-333333333333");
        _api.PatchTenantBrandingSettingsDocumentAsync(
                Arg.Any<PatchTenantBrandingSettingsDocumentDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateDocument(updatedStamp, "Updated Tenant", "https://cdn.example.test/updated.css"));
        var model = new TenantBrandingSettingsAdminModel
        {
            Exists = true,
            CanReplace = true,
            CanChangeDisplayName = true,
            ConcurrencyStamp = expectedStamp,
            DisplayName = " Updated Tenant ",
            CustomCssUrl = " https://cdn.example.test/tenant.css "
        };

        var result = await _service.PatchDisplayNameAsync(model);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Model).IsNotNull();
        await Assert.That(result.Model!.ConcurrencyStamp).IsEqualTo(updatedStamp);
        await Assert.That(result.Model.DisplayName).IsEqualTo("Updated Tenant");
        await _api.Received(1).PatchTenantBrandingSettingsDocumentAsync(
            Arg.Is<PatchTenantBrandingSettingsDocumentDto>(request =>
                IsDisplayNameOnlyRequest(request!, expectedStamp)),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AssetPatchMethods_SendOnlySelectedAssetLeaf()
    {
        var patches = new List<PatchTenantBrandingSettingsDocumentDto>();
        _api.PatchTenantBrandingSettingsDocumentAsync(
                Arg.Do<PatchTenantBrandingSettingsDocumentDto>(patches.Add),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateDocument(Guid.NewGuid(), "Tenant", "https://cdn.example.test/custom.css"));
        var model = new TenantBrandingSettingsAdminModel
        {
            Exists = true,
            CanReplace = true,
            CanChangeLogoUrl = true,
            CanChangeFaviconUrl = true,
            CanChangeCustomCssUrl = true,
            ConcurrencyStamp = Guid.NewGuid(),
            LogoUrl = " https://cdn.example.test/logo.svg ",
            FaviconUrl = " https://cdn.example.test/favicon.ico ",
            CustomCssUrl = " https://cdn.example.test/custom.css "
        };

        await _service.PatchLogoUrlAsync(model);
        await _service.PatchFaviconUrlAsync(model);
        await _service.PatchCustomCssUrlAsync(model);

        await Assert.That(patches.Count).IsEqualTo(3);
        PatchTenantBrandingAssetsDto firstAssets = patches[0].Assets
            ?? throw new InvalidOperationException("Logo request did not include the assets group.");
        PatchTenantBrandingAssetsDto secondAssets = patches[1].Assets
            ?? throw new InvalidOperationException("Favicon request did not include the assets group.");
        PatchTenantBrandingAssetsDto thirdAssets = patches[2].Assets
            ?? throw new InvalidOperationException("Custom CSS request did not include the assets group.");
        await Assert.That(patches[0].DisplayName).IsNull();
        await Assert.That(firstAssets.LogoUrl?.Value).IsEqualTo("https://cdn.example.test/logo.svg");
        await Assert.That(firstAssets.FaviconUrl).IsNull();
        await Assert.That(firstAssets.CustomCssUrl).IsNull();
        await Assert.That(secondAssets.LogoUrl).IsNull();
        await Assert.That(secondAssets.FaviconUrl?.Value).IsEqualTo("https://cdn.example.test/favicon.ico");
        await Assert.That(secondAssets.CustomCssUrl).IsNull();
        await Assert.That(thirdAssets.LogoUrl).IsNull();
        await Assert.That(thirdAssets.FaviconUrl).IsNull();
        await Assert.That(thirdAssets.CustomCssUrl?.Value).IsEqualTo("https://cdn.example.test/custom.css");
    }

    [Test]
    public async Task PatchDisplayNameAsync_WhenConcurrencyConflicts_ReturnsConflictWithoutRetry()
    {
        _api.PatchTenantBrandingSettingsDocumentAsync(
                Arg.Any<PatchTenantBrandingSettingsDocumentDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantBrandingSettingsDocumentDto>>(_ => throw new ApiException(
                "Conflict",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var model = new TenantBrandingSettingsAdminModel
        {
            Exists = true,
            CanReplace = true,
            CanChangeDisplayName = true,
            ConcurrencyStamp = Guid.NewGuid()
        };

        var result = await _service.PatchDisplayNameAsync(model);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.IsConcurrencyConflict).IsTrue();
        await _api.Received(1).PatchTenantBrandingSettingsDocumentAsync(
            Arg.Any<PatchTenantBrandingSettingsDocumentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private static HalResourceOfTenantBrandingSettingsDocumentDto CreateDocument(
        Guid stamp,
        string displayName,
        string customCssUrl) => new()
        {
            ConcurrencyStamp = stamp,
            Payload = new Payload
            {
                DisplayName = displayName,
                CustomCssUrl = customCssUrl
            },
            CanChangeDisplayName = true,
            CanChangeLogoUrl = true,
            CanChangeFaviconUrl = true,
            CanChangeCustomCssUrl = true,
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new()
                {
                    Href = "/api/tenant/settings/documents/branding",
                    Method = "PATCH"
                }
            }
        };

    private static bool IsDisplayNameOnlyRequest(
        PatchTenantBrandingSettingsDocumentDto request,
        Guid expectedStamp)
    {
        OptionalUpdateOfstring? displayName = request.DisplayName?.Value;
        return request.ExpectedConcurrencyStamp == expectedStamp
            && displayName?.HasValue == true
            && displayName.Value == "Updated Tenant"
            && request.Assets is null;
    }
}
