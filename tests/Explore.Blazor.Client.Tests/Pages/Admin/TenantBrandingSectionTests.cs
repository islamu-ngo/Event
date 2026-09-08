using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantBrandingSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly ITenantBrandingSettingsAdminService _service;

    public TenantBrandingSectionTests()
    {
        _service = _ctx.AddMockService<ITenantBrandingSettingsAdminService>();
        _service.PatchDisplayNameAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(call => Success(call.ArgAt<TenantBrandingSettingsAdminModel>(0)));
        _service.PatchLogoUrlAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(call => Success(call.ArgAt<TenantBrandingSettingsAdminModel>(0)));
        _service.PatchFaviconUrlAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(call => Success(call.ArgAt<TenantBrandingSettingsAdminModel>(0)));
        _service.PatchCustomCssUrlAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(call => Success(call.ArgAt<TenantBrandingSettingsAdminModel>(0)));
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task TenantBrandingSection_WhenReplaceLinkMissing_RendersReadOnlyMessage()
    {
        TenantBrandingSettingsAdminModel model = new()
        {
            Exists = true,
            CanReplace = false,
            DisplayName = "Read Only Tenant",
            CustomCssUrl = "https://cdn.example.test/tenant.css"
        };

        IRenderedComponent<TenantBrandingSection> cut = _ctx.Render<TenantBrandingSection>(parameters => parameters
            .Add(component => component.Model, model));

        await Assert.That(cut.Markup).Contains("API did not emit the replace-settings action");
        await Assert.That(cut.Markup).Contains("Read Only Tenant");
        await Assert.That(cut.Markup).Contains("https://cdn.example.test/tenant.css");
    }

    [Test]
    public async Task TenantBrandingSection_WhenDocumentMissing_RendersAlert()
    {
        TenantBrandingSettingsAdminModel model = TenantBrandingSettingsAdminModel.Missing();

        IRenderedComponent<TenantBrandingSection> cut = _ctx.Render<TenantBrandingSection>(parameters => parameters
            .Add(component => component.Model, model));

        await Assert.That(cut.Markup).Contains("Tenant branding settings have not been initialized.");
    }

    [Test]
    public async Task FieldCapabilities_DisableOnlyLockedFields()
    {
        var model = CreateEditableModel();
        model.CanChangeLogoUrl = false;

        var cut = Render(model);

        await Assert.That(Field(cut, "Brand display name").Instance.Disabled).IsFalse();
        await Assert.That(Field(cut, "Brand logo URL").Instance.Disabled).IsTrue();
        await Assert.That(Field(cut, "Brand favicon URL").Instance.Disabled).IsFalse();
        await Assert.That(Field(cut, "Brand custom CSS URL").Instance.Disabled).IsFalse();
    }

    [Test]
    public async Task DisplayName_BlurFlushesAutosaveWithPersistentAccessibleStatus()
    {
        var model = CreateEditableModel();
        var cut = Render(model);
        var field = Field(cut, "Brand display name");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Updated tenant"));
        field.Find("input").Blur();
        cut.WaitForState(() => cut.Find("[role='status']").TextContent.Contains("saved", StringComparison.OrdinalIgnoreCase));

        await _service.Received(1).PatchDisplayNameAsync(model, Arg.Any<CancellationToken>());
        await _service.DidNotReceive().PatchLogoUrlAsync(model, Arg.Any<CancellationToken>());
        var status = cut.Find("[role='status']");
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(status.GetAttribute("aria-atomic")).IsEqualTo("true");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task SerializedLeafSaves_ChainReturnedConcurrencyStamp()
    {
        Guid firstStamp = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid secondStamp = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid thirdStamp = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid? logoObservedStamp = null;
        var model = CreateEditableModel(firstStamp);
        _service.PatchDisplayNameAsync(model, Arg.Any<CancellationToken>())
            .Returns(_ => TenantBrandingSettingsSaveResult.Successful(Clone(model, secondStamp)));
        _service.PatchLogoUrlAsync(model, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                TenantBrandingSettingsAdminModel received = call.ArgAt<TenantBrandingSettingsAdminModel>(0);
                logoObservedStamp = received.ConcurrencyStamp;
                return TenantBrandingSettingsSaveResult.Successful(Clone(received, thirdStamp));
            });
        var cut = Render(model);

        var displayName = Field(cut, "Brand display name");
        await cut.InvokeAsync(() => displayName.Instance.ValueChanged.InvokeAsync("Updated tenant"));
        displayName.Find("input").Blur();
        cut.WaitForState(() => model.ConcurrencyStamp == secondStamp);
        var logo = Field(cut, "Brand logo URL");
        await cut.InvokeAsync(() => logo.Instance.ValueChanged.InvokeAsync("https://cdn.example.test/new-logo.svg"));
        logo.Find("input").Blur();
        cut.WaitForState(() => model.ConcurrencyStamp == thirdStamp);

        await Assert.That(logoObservedStamp).IsEqualTo(secondStamp);
    }

    [Test]
    public async Task FailedSave_RetainsLocalValueAndShowsAccessibleError()
    {
        _service.PatchDisplayNameAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(TenantBrandingSettingsSaveResult.Failed("Branding save failed."));
        var model = CreateEditableModel();
        var cut = Render(model);
        var field = Field(cut, "Brand display name");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Unsaved tenant"));
        field.Find("input").Blur();
        cut.WaitForState(() => cut.Find("[role='alert']").TextContent.Contains("failed", StringComparison.OrdinalIgnoreCase));

        await Assert.That(model.DisplayName).IsEqualTo("Unsaved tenant");
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Branding save failed.");
    }

    [Test]
    public async Task ConcurrencyConflict_ReloadsAuthoritativeModelAndSurfacesError()
    {
        Guid authoritativeStamp = Guid.NewGuid();
        _service.PatchDisplayNameAsync(Arg.Any<TenantBrandingSettingsAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(TenantBrandingSettingsSaveResult.Conflict());
        _service.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CreateEditableModel(authoritativeStamp, "Authoritative tenant"));
        var model = CreateEditableModel();
        var cut = Render(model);
        var field = Field(cut, "Brand display name");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Conflicting tenant"));
        field.Find("input").Blur();
        cut.WaitForState(() => model.ConcurrencyStamp == authoritativeStamp);

        await Assert.That(model.DisplayName).IsEqualTo("Authoritative tenant");
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("changed elsewhere");
        await _service.Received(1).GetAsync(Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<TenantBrandingSection> Render(TenantBrandingSettingsAdminModel model) =>
        _ctx.RenderMudComponent<TenantBrandingSection>(parameters => parameters.Add(component => component.Model, model));

    private static IRenderedComponent<MudTextField<string>> Field(
        IRenderedComponent<TenantBrandingSection> cut,
        string label) => cut.FindComponents<MudTextField<string>>().Single(field => field.Instance.Label == label);

    private static TenantBrandingSettingsAdminModel CreateEditableModel(
        Guid? stamp = null,
        string displayName = "Tenant") => new()
        {
            Exists = true,
            CanReplace = true,
            CanChangeDisplayName = true,
            CanChangeLogoUrl = true,
            CanChangeFaviconUrl = true,
            CanChangeCustomCssUrl = true,
            ConcurrencyStamp = stamp ?? Guid.NewGuid(),
            DisplayName = displayName,
            LogoUrl = "https://cdn.example.test/logo.svg",
            FaviconUrl = "https://cdn.example.test/favicon.ico",
            CustomCssUrl = "https://cdn.example.test/custom.css"
        };

    private static TenantBrandingSettingsSaveResult Success(TenantBrandingSettingsAdminModel model) =>
        TenantBrandingSettingsSaveResult.Successful(Clone(model, Guid.NewGuid()));

    private static TenantBrandingSettingsAdminModel Clone(
        TenantBrandingSettingsAdminModel model,
        Guid stamp) => new()
        {
            Exists = model.Exists,
            CanReplace = model.CanReplace,
            CanChangeDisplayName = model.CanChangeDisplayName,
            CanChangeLogoUrl = model.CanChangeLogoUrl,
            CanChangeFaviconUrl = model.CanChangeFaviconUrl,
            CanChangeCustomCssUrl = model.CanChangeCustomCssUrl,
            ConcurrencyStamp = stamp,
            DisplayName = model.DisplayName,
            LogoUrl = model.LogoUrl,
            FaviconUrl = model.FaviconUrl,
            CustomCssUrl = model.CustomCssUrl
        };
}
