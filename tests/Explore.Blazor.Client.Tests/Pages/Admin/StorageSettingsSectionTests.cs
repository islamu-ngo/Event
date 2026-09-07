using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;
using InstanceStorageRouteDto = Explore.Blazor.Client.Clients.Routes;
using TenantStorageRouteDto = Explore.Blazor.Client.Clients.Routes2;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class StorageSettingsSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ITenantStorageSettingsAdminService _storageService;

    public StorageSettingsSectionTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Storage Admin", "admin@example.com");
        _storageService = _ctx.AddMockService<ITenantStorageSettingsAdminService>();
        _storageService.PatchPolicyAsync(
                Arg.Any<HalResourceOfTenantStorageSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _storageService.PatchS3Async(
                Arg.Any<HalResourceOfTenantStorageSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task InstanceStorageSection_WithHalActions_RendersProviderActions()
    {
        // Arrange
        var onboardingService = _ctx.AddMockService<IInstanceOnboardingService>();
        var model = new HalResourceOfInstanceStorageSettingsDto
        {
            Provider = StorageProviderOptions.Local,
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/instance/settings/storage", Method = "PUT" },
                ["provider-test"] = new() { Href = "/api/instance/settings/storage/test", Method = "POST" },
                ["recalculate-usage"] = new() { Href = "/api/instance/settings/storage/recalculate-usage", Method = "POST" }
            }
        };

        // Act
        var cut = RenderInstanceStorage(model, onboardingService);

        // Assert
        await Assert.That(cut.Markup).Contains("Test Provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Recalculate Usage", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceStorageSection_WithRoutes_RendersHybridRouteMatrix()
    {
        // Arrange
        var model = new HalResourceOfInstanceStorageSettingsDto
        {
            Provider = StorageProviderOptions.Local,
            Routes =
            [
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.Images, Provider = StorageProviderOptions.Local, MaxUploadBytes = 8L * 1024 * 1024 },
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.Documents, Provider = StorageProviderOptions.S3Compatible, MaxUploadBytes = 64L * 1024 * 1024 },
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.General, Provider = StorageProviderOptions.Local, MaxUploadBytes = 16L * 1024 * 1024 }
            ],
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/instance/settings/storage", Method = "PUT" }
            }
        };

        // Act
        var cut = RenderInstanceStorage(model);

        // Assert
        await Assert.That(cut.Markup).Contains("Hybrid storage routes", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Images", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Documents", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("General uploads", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceStorageSection_WithoutEditHalLink_RendersReadOnlyState()
    {
        // Arrange
        var model = new HalResourceOfInstanceStorageSettingsDto
        {
            Provider = StorageProviderOptions.Local
        };

        // Act
        var cut = RenderInstanceStorage(model);

        // Assert
        await Assert.That(cut.Markup).Contains("read-only", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("Test Provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("Recalculate Usage", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task TenantStorageSection_WhenLocked_RendersReadOnlyPolicyMessage()
    {
        // Arrange
        var model = new HalResourceOfTenantStorageSettingsDto
        {
            TenantOverridesAllowed = false,
            TenantStorageLocked = true,
            IsReadOnly = true,
            Provider = StorageProviderOptions.Local
        };

        // Act
        var cut = RenderTenantStorage(model);

        // Assert
        await Assert.That(cut.Markup).Contains("read-only", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("inherited from platform policy", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task TenantStorageSection_WhenEditable_RendersOverrideFields()
    {
        // Arrange
        var model = new HalResourceOfTenantStorageSettingsDto
        {
            TenantOverridesAllowed = true,
            TenantStorageLocked = false,
            IsReadOnly = false,
            Provider = StorageProviderOptions.Local,
            EffectivePolicy = new EffectivePolicy2
            {
                Provider = StorageProviderOptions.Local,
                InstanceMaxUploadBytes = 100L * 1024 * 1024,
                MaxUploadBytes = 10L * 1024 * 1024,
                TenantQuotaBytes = 1024L * 1024 * 1024
            },
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/tenant/settings/storage", Method = "PATCH" }
            }
        };

        // Act
        var cut = RenderTenantStorage(model);

        // Assert
        await Assert.That(cut.Markup).Contains("Tenant Storage Provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Max upload", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Tenant quota", StringComparison.OrdinalIgnoreCase);
        var status = cut.Find("[role='status']");
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(status.GetAttribute("aria-atomic")).IsEqualTo("true");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task TenantStorageSection_ProviderAndRouteProvider_SavePolicyImmediately()
    {
        var model = CreateEditableTenantModel();
        var cut = RenderTenantStorage(model);
        var provider = Select(cut, "Tenant Storage Provider");
        var routeProvider = Select(cut, "Images provider");

        await cut.InvokeAsync(() => provider.Instance.ValueChanged.InvokeAsync(StorageProviderOptions.S3Compatible));
        await cut.InvokeAsync(() => routeProvider.Instance.ValueChanged.InvokeAsync(StorageProviderOptions.S3Compatible));

        await _storageService.Received(2).PatchPolicyAsync(model, Arg.Any<CancellationToken>());
        await _storageService.DidNotReceive().PatchS3Async(
            Arg.Any<HalResourceOfTenantStorageSettingsDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TenantStorageSection_S3TextBlurAndForcePathStyle_SaveOnlyS3()
    {
        var model = CreateEditableTenantModel(StorageProviderOptions.S3Compatible);
        var cut = RenderTenantStorage(model);
        var endpoint = TextField(cut, "Endpoint");

        await cut.InvokeAsync(() => endpoint.Instance.ValueChanged.InvokeAsync("https://new.example.test"));
        endpoint.Find("input").Blur();
        cut.WaitForState(() => model.S3Endpoint == "https://new.example.test");
        await cut.InvokeAsync(() => ForcePathSwitch(cut).Instance.ValueChanged.InvokeAsync(false));

        await _storageService.Received(2).PatchS3Async(model, Arg.Any<CancellationToken>());
        await _storageService.DidNotReceive().PatchPolicyAsync(model, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TenantStorageSection_FailedSaveRetainsLocalValue_AndNewestCompletionOwnsFeedback()
    {
        var firstRelease = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        _storageService.PatchPolicyAsync(
                Arg.Any<HalResourceOfTenantStorageSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(firstRelease.Task);
        _storageService.PatchS3Async(
                Arg.Any<HalResourceOfTenantStorageSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "Newest storage save failed." });
        var model = CreateEditableTenantModel(StorageProviderOptions.S3Compatible);
        var cut = RenderTenantStorage(model);

        Task first = cut.InvokeAsync(() => Select(cut, "Images provider").Instance.ValueChanged.InvokeAsync(StorageProviderOptions.S3Compatible));
        cut.WaitForState(() => cut.Find("[role='status']").TextContent.Contains("Saving", StringComparison.Ordinal));
        Task second = cut.InvokeAsync(() => ForcePathSwitch(cut).Instance.ValueChanged.InvokeAsync(false));
        await _storageService.DidNotReceive().PatchS3Async(model, Arg.Any<CancellationToken>());

        firstRelease.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await Task.WhenAll(first, second);

        await Assert.That(model.Provider).IsEqualTo(StorageProviderOptions.S3Compatible);
        await Assert.That(model.S3ForcePathStyle).IsFalse();
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Newest storage save failed.");
        await Assert.That(cut.Find("[role='status']").TextContent).DoesNotContain("saved", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task TenantStorageSection_WhenLocked_RendersRouteMatrixAsInheritedPolicy()
    {
        // Arrange
        var model = new HalResourceOfTenantStorageSettingsDto
        {
            TenantOverridesAllowed = false,
            TenantStorageLocked = true,
            IsReadOnly = true,
            Provider = StorageProviderOptions.Local,
            Routes =
            [
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.Images, Provider = StorageProviderOptions.Local, MaxUploadBytes = 8L * 1024 * 1024, IsReadOnly = true },
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.Documents, Provider = StorageProviderOptions.S3Compatible, MaxUploadBytes = 64L * 1024 * 1024, IsReadOnly = true },
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.General, Provider = StorageProviderOptions.Local, MaxUploadBytes = 16L * 1024 * 1024, IsReadOnly = true }
            ],
            EffectivePolicy = new EffectivePolicy2
            {
                Provider = StorageProviderOptions.Local,
                InstanceMaxUploadBytes = 100L * 1024 * 1024,
                MaxUploadBytes = 10L * 1024 * 1024,
                TenantQuotaBytes = 1024L * 1024 * 1024
            }
        };

        // Act
        var cut = RenderTenantStorage(model);

        // Assert
        await Assert.That(cut.Markup).Contains("Hybrid storage routes", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Tenant overrides stay bounded", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Documents", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task StorageSettingsDto_ToUpdateRequest_PreservesTypedRouteMatrix()
    {
        // Arrange
        var model = new HalResourceOfInstanceStorageSettingsDto
        {
            Routes =
            [
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.Images, Provider = StorageProviderOptions.Local, MaxUploadBytes = 8L * 1024 * 1024 },
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.Documents, Provider = StorageProviderOptions.S3Compatible, MaxUploadBytes = 64L * 1024 * 1024 },
                new InstanceStorageRouteDto { RouteKey = StorageRouteOptions.General, Provider = StorageProviderOptions.Local, MaxUploadBytes = 16L * 1024 * 1024 }
            ]
        };

        // Act
        var dto = model.ToUpdateRequest();

        // Assert
        await Assert.That(dto.Policy?.Value?.Routes).IsNotNull();
        await Assert.That(dto.Policy!.Value!.Routes!.Count).IsEqualTo(3);
        await Assert.That(dto.Policy.Value.Routes.Any(route => route.RouteKey == StorageRouteOptions.Documents && route.Provider == StorageProviderOptions.S3Compatible)).IsTrue();
        await Assert.That(dto.Policy.Value.Routes.Single(route => route.RouteKey == StorageRouteOptions.Documents).MaxUploadBytes)
            .IsEqualTo(64L * 1024 * 1024);
    }

    private IRenderedComponent<InstanceStorageSection> RenderInstanceStorage(
        HalResourceOfInstanceStorageSettingsDto model,
        IInstanceOnboardingService? onboardingService = null) =>
        _ctx.RenderMudComponent<InstanceStorageSection>(builder => builder
            .Add(component => component.Model, model)
            .Add(component => component.OnboardingService, onboardingService)
            .Add(component => component.IsSingleTenant, true));

    private IRenderedComponent<TenantStorageSection> RenderTenantStorage(HalResourceOfTenantStorageSettingsDto model) =>
        _ctx.RenderMudComponent<TenantStorageSection>(builder => builder.Add(component => component.Model, model));

    private static IRenderedComponent<MudSelect<string>> Select(
        IRenderedComponent<TenantStorageSection> cut,
        string label) => cut.FindComponents<MudSelect<string>>().Single(item => item.Instance.Label == label);

    private static IRenderedComponent<MudNumericField<long>> NumericLong(
        IRenderedComponent<TenantStorageSection> cut,
        string label) => cut.FindComponents<MudNumericField<long>>().Single(item => item.Instance.Label == label);

    private static IRenderedComponent<MudTextField<string>> TextField(
        IRenderedComponent<TenantStorageSection> cut,
        string label) => cut.FindComponents<MudTextField<string>>().Single(item => item.Instance.Label == label);

    private static IRenderedComponent<MudSwitch<bool>> ForcePathSwitch(IRenderedComponent<TenantStorageSection> cut) =>
        cut.FindComponents<MudSwitch<bool>>().Single(item =>
            item.Markup.Contains("Force path-style URLs", StringComparison.Ordinal));

    private static HalResourceOfTenantStorageSettingsDto CreateEditableTenantModel(
        string provider = StorageProviderOptions.Local) => new()
        {
            TenantOverridesAllowed = true,
            TenantStorageLocked = false,
            IsReadOnly = false,
            Provider = provider,
            MaxUploadBytes = 10L * 1024 * 1024,
            TenantQuotaBytes = 1024L * 1024 * 1024,
            S3ForcePathStyle = true,
            S3UploadUrlExpirationMinutes = 60,
            Routes =
            [
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.Images, Provider = StorageProviderOptions.Local, MaxUploadBytes = 8L * 1024 * 1024 },
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.Documents, Provider = StorageProviderOptions.Local, MaxUploadBytes = 16L * 1024 * 1024 },
                new TenantStorageRouteDto { RouteKey = StorageRouteOptions.General, Provider = StorageProviderOptions.Local, MaxUploadBytes = 10L * 1024 * 1024 }
            ],
            EffectivePolicy = new EffectivePolicy2 { InstanceMaxUploadBytes = 100L * 1024 * 1024 },
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/tenant/settings/storage", Method = "PATCH" }
            }
        };

}
