using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantDirectoryOperatorIdentitySectionTests
{
    [Test]
    public async Task Render_GroupsLegalContactDisclosureAndCommerceValues_WithDirectionalIslands()
    {
        using var context = new BlazorTestContext();
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut =
            Render(context, CreateModel(canEdit: true));

        await Assert.That(cut.Markup).Contains("Legal identity");
        await Assert.That(cut.Markup).Contains("Contact and support");
        await Assert.That(cut.Markup).Contains("Legal and disclosure links");
        await Assert.That(cut.Markup).Contains("Commerce readiness");
        await Assert.That(Fields(cut, "Public name").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Legal name").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Public contact email").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Legal notice URL").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Terms URL").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Privacy URL").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[dir='ltr']").Count).IsGreaterThanOrEqualTo(6);
        await Assert.That(cut.FindAll("[role='status'][aria-live='polite']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Save_IsRenderedAndEnabledOnlyWhenExactHalEditAffordanceWasMapped()
    {
        using var readOnlyContext = new BlazorTestContext();
        using var editableContext = new BlazorTestContext();

        IRenderedComponent<TenantDirectoryOperatorIdentitySection> readOnly =
            Render(readOnlyContext, CreateModel(canEdit: false));
        await Assert.That(SaveButtons(readOnly).Count).IsEqualTo(0);
        await Assert.That(readOnly.FindAll("input[readonly]").Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(readOnly.FindAll("input[disabled]").Count).IsEqualTo(0);
        await Assert.That(readOnly.FindAll("input[readonly]")
            .All(input => input.GetAttribute("tabindex") != "-1")).IsTrue();

        IRenderedComponent<TenantDirectoryOperatorIdentitySection> editable =
            Render(editableContext, CreateModel(canEdit: true));
        IReadOnlyList<AngleSharp.Dom.IElement> saveButtons = SaveButtons(editable);
        await Assert.That(saveButtons.Count).IsEqualTo(1);
        await Assert.That(saveButtons[0].HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task InvalidSubmit_ShowsAlertMarksFieldsAndFocusesFirstInvalid()
    {
        using var context = new BlazorTestContext();
        TenantDirectoryOperatorIdentityAdminModel model = CreateModel(canEdit: true);
        model.PublicName = string.Empty;
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut = Render(context, model);
        IAccessibilityFocusService focus =
            context.Services.GetRequiredService<IAccessibilityFocusService>();

        await cut.Find("[data-testid='save-tenant-directory-operator-identity']")
            .ClickAsync(new());

        await Assert.That(cut.FindAll(
            "[data-testid='operator-validation-summary'][role='alert']"))
            .HasSingleItem();
        await Assert.That(cut.Find("#operator-public-name").GetAttribute("aria-invalid"))
            .IsEqualTo("true");
        await focus.Received(1).FocusAsync("#operator-public-name");
    }

    [Test]
    public async Task Conflict_PreservesPendingEditsShowsAuthoritativeValuesAndFocusesAlert()
    {
        TenantDirectoryOperatorIdentityAdminModel pending = CreateModel(canEdit: true);
        pending.PublicName = "My pending edit";
        TenantDirectoryOperatorIdentityAdminModel authoritative = CreateModel(canEdit: true);
        authoritative.PublicName = "Authoritative value";
        authoritative.ConcurrencyStamp = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var context = new BlazorTestContext();
        context.Services.AddSingleton<ITenantDirectoryOperatorIdentityAdminService>(
            new ConflictService(authoritative));
        IAccessibilityFocusService focus =
            context.Services.GetRequiredService<IAccessibilityFocusService>();
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut =
            Render(context, pending);

        await cut.Find("[data-testid='save-tenant-directory-operator-identity']")
            .ClickAsync(new());

        await Assert.That(pending.PublicName).IsEqualTo("My pending edit");
        await Assert.That(pending.ConcurrencyStamp).IsEqualTo(authoritative.ConcurrencyStamp);
        await Assert.That(cut.Find("[data-testid='operator-concurrency-conflict']").TextContent)
            .Contains("Authoritative value");
        await focus.Received(1).FocusAsync("#operator-conflict-alert");
    }

    [Test]
    public async Task ConflictReloadFailure_PreservesPendingRevisionAndOmitsBlankAuthorityPanel()
    {
        TenantDirectoryOperatorIdentityAdminModel pending = CreateModel(canEdit: true);
        Guid pendingRevision = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        pending.ConcurrencyStamp = pendingRevision;
        pending.PublicName = "My pending edit";
        using var context = new BlazorTestContext();
        context.Services.AddSingleton<ITenantDirectoryOperatorIdentityAdminService>(
            new ConflictService(TenantDirectoryOperatorIdentityAdminModel.Failed()));
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut =
            Render(context, pending);

        await cut.Find("[data-testid='save-tenant-directory-operator-identity']")
            .ClickAsync(new());

        await Assert.That(pending.PublicName).IsEqualTo("My pending edit");
        await Assert.That(pending.ConcurrencyStamp).IsEqualTo(pendingRevision);
        await Assert.That(cut.FindAll("[data-testid='operator-concurrency-conflict']"))
            .IsEmpty();
    }

    [Test]
    public async Task FirstInvalidFocusOrder_IsObservableThroughRenderedSubmission()
    {
        using var context = new BlazorTestContext();
        TenantDirectoryOperatorIdentityAdminModel model = CreateModel(canEdit: true);
        model.PublicName = string.Empty;
        model.LegalName = string.Empty;
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut = Render(context, model);
        IAccessibilityFocusService focus =
            context.Services.GetRequiredService<IAccessibilityFocusService>();

        await cut.Find("[data-testid='save-tenant-directory-operator-identity']")
            .ClickAsync(new());

        await focus.Received(1).FocusAsync("#operator-public-name");
    }

    [Test]
    public async Task EveryBoundedMessageCode_RendersCopy_AndUnknownCodesFailLoudly()
    {
        foreach (TenantDirectoryOperatorIdentityAdminMessageCode code in
                 Enum.GetValues<TenantDirectoryOperatorIdentityAdminMessageCode>()
                     .Where(code => code != TenantDirectoryOperatorIdentityAdminMessageCode.None))
        {
            using var context = new BlazorTestContext();
            TenantDirectoryOperatorIdentityAdminModel model =
                TenantDirectoryOperatorIdentityAdminModel.Missing();
            model.MessageCode = code;

            IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut =
                Render(context, model);

            await Assert.That(cut.Markup).DoesNotContain(code.ToString());
            await Assert.That(cut.Find("[role='alert']").TextContent).IsNotEmpty();
        }

        using var invalidContext = new BlazorTestContext();
        TenantDirectoryOperatorIdentityAdminModel invalid =
            TenantDirectoryOperatorIdentityAdminModel.Missing();
        invalid.MessageCode = (TenantDirectoryOperatorIdentityAdminMessageCode)int.MaxValue;
        Exception? observed = null;
        try
        {
            _ = Render(invalidContext, invalid);
        }
        catch (Exception exception)
        {
            observed = exception.GetBaseException();
        }

        await Assert.That(observed).IsTypeOf<ArgumentOutOfRangeException>();
    }

    private sealed class ConflictService(
        TenantDirectoryOperatorIdentityAdminModel authoritative)
        : ITenantDirectoryOperatorIdentityAdminService
    {
        public Task<TenantDirectoryOperatorIdentityAdminModel> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(authoritative);

        public Task<TenantDirectoryOperatorIdentitySaveResult> SaveAsync(
            TenantDirectoryOperatorIdentityAdminModel model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantDirectoryOperatorIdentitySaveResult.Conflict(authoritative));
    }

    private static IRenderedComponent<TenantDirectoryOperatorIdentitySection> Render(
        BlazorTestContext context,
        TenantDirectoryOperatorIdentityAdminModel model)
    {
        if (!context.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(ITenantDirectoryOperatorIdentityAdminService)))
        {
            context.Services.AddSingleton(
                Substitute.For<ITenantDirectoryOperatorIdentityAdminService>());
        }

        return context.RenderMudComponent<TenantDirectoryOperatorIdentitySection>(
            parameters => parameters.Add(component => component.Model, model));
    }

    private static TenantDirectoryOperatorIdentityAdminModel CreateModel(bool canEdit) => new()
    {
        Exists = true,
        CanEdit = canEdit,
        ConcurrencyStamp = Guid.Parse("66666666-6666-6666-6666-666666666666"),
        PublicName = "Community Directory",
        LegalName = "Community Directory Foundation",
        OperatorKindCode = "NONPROFIT",
        JurisdictionCountryCode = "DE",
        RegistrationIdentifier = "VR 12345",
        PublicContactEmail = "support@directory.example",
        LegalNoticeUrl = "https://directory.example/legal",
        TermsUrl = "https://directory.example/terms",
        PrivacyUrl = "https://directory.example/privacy",
        IsActivationReady = true,
        IsPublicDisclosureReady = true,
        IsPaidCommerceReady = true
    };

    private static IReadOnlyList<IRenderedComponent<MudTextField<string>>> Fields(
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut,
        string label) => cut.FindComponents<MudTextField<string>>()
            .Where(field => field.Instance.Label == label)
            .ToArray();

    private static IReadOnlyList<AngleSharp.Dom.IElement> SaveButtons(
        IRenderedComponent<TenantDirectoryOperatorIdentitySection> cut) =>
        cut.FindAll("button")
            .Where(button => button.TextContent.Trim()
                .Equals("Save", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
