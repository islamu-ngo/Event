using AngleSharp.Dom;
using Explore.Blazor.Client.Components;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services;
using MudBlazor;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Instance;

public sealed class InstanceOperatorIdentitySectionTests
{
    [Test]
    public async Task Render_WhenCanEditIsTrue_RendersSaveButtonAndEditableInputs()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);
        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(model));
        context.Services.AddSingleton(service);

        var cut = context.RenderMudComponent<InstanceOperatorIdentitySection>();

        await Assert.That(cut.FindAll("[data-testid='save-operator-identity']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[readonly]").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-testid='operator-read-only-alert']")).IsEmpty();
    }

    [Test]
    public async Task Render_WhenCanEditIsFalse_OmitsSaveButtonAndSetsInputsReadOnly()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: false);
        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(model));
        context.Services.AddSingleton(service);

        var cut = context.RenderMudComponent<InstanceOperatorIdentitySection>();

        await Assert.That(cut.FindAll("[data-testid='save-operator-identity']")).IsEmpty();
        await Assert.That(cut.FindAll("input[readonly]").Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(cut.FindAll("[data-testid='operator-read-only-alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Save_WhenConflictOccurs_ShowsAuthoritativeValuesAndPreservesPendingEdits()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);
        model.PublicName = "My pending edit";

        var authoritative = CreateModel(canEdit: true);
        authoritative.PublicName = "Authoritative value";
        authoritative.Revision = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(model));
        service.SaveAsync(Arg.Any<InstanceOperatorIdentityAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstanceOperatorIdentitySaveResult.Conflict(authoritative)));
        context.Services.AddSingleton(service);

        var cut = context.RenderMudComponent<InstanceOperatorIdentitySection>();

        await cut.Find("[data-testid='save-operator-identity']").ClickAsync(new());

        await Assert.That(cut.FindAll("[data-testid='operator-concurrency-conflict']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='operator-concurrency-conflict']").TextContent).Contains("Authoritative value");
        await Assert.That(model.PublicName).IsEqualTo("My pending edit");
        await Assert.That(model.Revision).IsEqualTo(authoritative.Revision);
    }

    [Test]
    public async Task Save_WhenSuccessful_UpdatesRevisionAndShowsSuccessStatus()
    {
        using var context = new BlazorTestContext();
        var model = CreateModel(canEdit: true);
        var updated = CreateModel(canEdit: true);
        updated.Revision = Guid.Parse("88888888-8888-8888-8888-888888888888");

        var service = Substitute.For<IInstanceOperatorIdentityAdminService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(model));
        service.SaveAsync(Arg.Any<InstanceOperatorIdentityAdminModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstanceOperatorIdentitySaveResult.Successful(updated)));
        context.Services.AddSingleton(service);

        var cut = context.RenderMudComponent<InstanceOperatorIdentitySection>();

        await cut.Find("[data-testid='save-operator-identity']").ClickAsync(new());

        await Assert.That(model.Revision).IsEqualTo(updated.Revision);
        await Assert.That(cut.Markup).Contains("Operator identity saved.");
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
}
