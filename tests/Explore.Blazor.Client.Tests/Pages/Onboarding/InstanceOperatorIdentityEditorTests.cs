using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Onboarding;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services;
using MudBlazor;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class InstanceOperatorIdentityEditorTests
{
    [Test]
    public async Task Render_DisplaysAllRequiredFieldsWithMudBlazorLabelsAndAccessibilityAttributes()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);
        var cut = Render(context, model);

        await Assert.That(cut.Markup).Contains("Legal identity");
        await Assert.That(cut.Markup).Contains("Contact and official presence");
        await Assert.That(cut.Markup).Contains("Legal and disclosure links");
        await Assert.That(cut.Markup).Contains("Readiness status");

        await Assert.That(Fields(cut, "Public name").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Legal name").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Operator kind code").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Jurisdiction country code").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Registration identifier").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Public contact email").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Website URL").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Official origin").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Legal notice URL").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Terms URL").Count).IsEqualTo(1);
        await Assert.That(Fields(cut, "Privacy URL").Count).IsEqualTo(1);

        await Assert.That(cut.FindAll("[dir='ltr']").Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(cut.FindAll("[data-testid='operator-readiness-ready']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task SaveDraft_WithIncompleteDraft_InvokesAdminServiceAndUpdatesReadinessState()
    {
        using var context = new BlazorTestContext();
        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        var model = CreateModel(canEdit: true);
        model.PaidCommerceIsReady = false;
        model.ReasonCodes = new List<string> { "instance_operator_identity_website_url_missing" };

        service.SaveAsync(Arg.Any<InstanceOperatorIdentityAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<InstanceOperatorIdentityAdminModel>();
                input.Revision = Guid.Parse("11111111-1111-1111-1111-111111111111");
                input.PaidCommerceIsReady = false;
                input.ReasonCodes = new List<string> { "instance_operator_identity_website_url_missing" };
                return Task.FromResult(InstanceOperatorIdentitySaveResult.Successful(input));
            });

        context.Services.AddSingleton(service);
        var cut = Render(context, model, allowIncompleteDraft: true);

        await cut.Find("[data-testid='save-operator-identity']").ClickAsync(new());

        await service.Received(1).SaveAsync(Arg.Is<InstanceOperatorIdentityAdminModel>(m => m.PublicName == model.PublicName), Arg.Any<CancellationToken>());
        await Assert.That(cut.FindAll("[data-testid='operator-readiness-incomplete']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("instance_operator_identity_website_url_missing");
    }

    [Test]
    public async Task SaveDraft_WithValidationErrors_DisplaysInlineFieldErrorsAndAccessibleSummary()
    {
        using var context = new BlazorTestContext();
        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        var model = CreateModel(canEdit: true);

        service.SaveAsync(Arg.Any<InstanceOperatorIdentityAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstanceOperatorIdentitySaveResult.Failed(
                InstanceOperatorIdentityAdminMessageCode.SaveFailed,
                "Validation failed",
                new Dictionary<string, string> { [nameof(InstanceOperatorIdentityAdminModel.PublicName)] = "Public name is invalid." })));

        context.Services.AddSingleton(service);
        var cut = Render(context, model, allowIncompleteDraft: true);

        await cut.Find("[data-testid='save-operator-identity']").ClickAsync(new());

        await Assert.That(cut.FindAll("[data-testid='operator-validation-summary']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("#instance-operator-public-name").GetAttribute("aria-invalid")).IsEqualTo("true");
    }

    [Test]
    public async Task SingleTenantMode_RendersCopyToDirectoryIdentityButton_AndInvokesCallback()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);
        InstanceOperatorIdentityAdminModel? copied = null;

        var cut = Render(
            context,
            model,
            isSingleTenant: true,
            showCopyToDirectoryIdentity: true,
            onCopyToDirectoryIdentity: (InstanceOperatorIdentityAdminModel m) => copied = m);

        var copyButton = cut.Find("[data-testid='copy-to-directory-identity']");
        await Assert.That(copyButton).IsNotNull();

        await copyButton.ClickAsync(new());
        await Assert.That(copied).IsNotNull();
        await Assert.That(copied!.PublicName).IsEqualTo(model.PublicName);
        await Assert.That(copied.LegalName).IsEqualTo(model.LegalName);
    }

    [Test]
    public async Task MultiTenantMode_DoesNotRenderCopyToDirectoryIdentityButton()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);

        var cut = Render(
            context,
            model,
            isSingleTenant: false,
            showCopyToDirectoryIdentity: false);

        await Assert.That(cut.FindAll("[data-testid='copy-to-directory-identity']")).IsEmpty();
    }

    [Test]
    public async Task ReadOnly_OmitsSaveButtonAndDisablesInputs()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: false);

        var cut = Render(context, model);

        await Assert.That(cut.FindAll("[data-testid='save-operator-identity']")).IsEmpty();
        await Assert.That(cut.FindAll("input[readonly]").Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(cut.FindAll("[data-testid='operator-read-only-alert']").Count).IsEqualTo(1);
    }

    private static IRenderedComponent<InstanceOperatorIdentityEditor> Render(
        BlazorTestContext context,
        InstanceOperatorIdentityAdminModel model,
        bool isSingleTenant = false,
        bool allowIncompleteDraft = false,
        bool showCopyToDirectoryIdentity = false,
        Action<InstanceOperatorIdentityAdminModel>? onCopyToDirectoryIdentity = null)
    {
        if (!context.Services.Any(d => d.ServiceType == typeof(IInstanceOperatorIdentityAdminService)))
        {
            context.Services.AddSingleton(Substitute.For<IInstanceOperatorIdentityAdminService>());
        }

        return context.RenderMudComponent<InstanceOperatorIdentityEditor>(parameters =>
        {
            parameters.Add(c => c.Model, model);
            parameters.Add(c => c.IsSingleTenantMode, isSingleTenant);
            parameters.Add(c => c.AllowIncompleteDraft, allowIncompleteDraft);
            parameters.Add(c => c.ShowCopyToDirectoryIdentity, showCopyToDirectoryIdentity);
            if (onCopyToDirectoryIdentity is not null)
            {
                parameters.Add(c => c.OnCopyToDirectoryIdentity, onCopyToDirectoryIdentity);
            }
        });
    }

    private static InstanceOperatorIdentityAdminModel CreateModel(bool canEdit) => new()
    {
        Exists = true,
        CanEdit = canEdit,
        Revision = Guid.Parse("77777777-7777-7777-7777-777777777777"),
        OperatorId = Guid.Parse("018f4350-1234-789a-b0cd-ef0123456789"),
        PublicName = "Global Event Network",
        LegalName = "Global Event Network gGmbH",
        OperatorKindCode = "NONPROFIT",
        JurisdictionCountryCode = "DE",
        RegistrationIdentifier = "HRB 98765",
        PublicContactEmail = "contact@event.example",
        WebsiteUrl = "https://event.example",
        OfficialOrigin = "https://event.example",
        LegalNoticeUrl = "https://event.example/imprint",
        TermsUrl = "https://event.example/terms",
        PrivacyUrl = "https://event.example/privacy",
        IsOfficialInstance = true,
        PaidCommerceIsReady = true,
        ReasonCodes = new List<string>()
    };

    private static IReadOnlyList<IRenderedComponent<MudTextField<string>>> Fields(
        IRenderedComponent<InstanceOperatorIdentityEditor> cut,
        string label) => cut.FindComponents<MudTextField<string>>()
            .Where(f => f.Instance.Label == label)
            .ToArray();
}
