using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class ParticipationConfigurationEditorTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IAccessibilityAnnouncerService _announcer;

    public ParticipationConfigurationEditorTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _announcer = _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_UsesCurrentTypedConfigurationAsInitialValues()
    {
        var cut = RenderEditor(CreateConfiguration(
            handling: 4,
            advance: 3,
            identity: 2,
            recovery: 2));

        await Assert.That(Select(cut, "Participation handling").Instance.Value).IsEqualTo(4);
        await Assert.That(Select(cut, "Advance registration").Instance.Value).IsEqualTo(3);
        await Assert.That(Select(cut, "Identity access").Instance.Value).IsEqualTo(2);
        await Assert.That(Select(cut, "Guest recovery").Instance.Value).IsEqualTo(2);
        await Assert.That(cut.Find("h3").TextContent).IsEqualTo("Participation configuration");
    }

    [Test]
    public async Task ModeChanges_RenderOnlyDomainLegalDependentFields()
    {
        var cut = RenderEditor(CreateConfiguration(4, 3, 2, 1));
        var handling = Select(cut, "Participation handling");

        await cut.InvokeAsync(() => handling.Instance.ValueChanged.InvokeAsync(1));
        await Assert.That(Select(cut, "Advance registration").Instance.Value).IsEqualTo(1);
        await Assert.That(Select(cut, "Advance registration").Instance.Disabled).IsTrue();
        await Assert.That(cut.FindComponents<MudSelect<int?>>().Any(item => item.Instance.Label == "Identity access")).IsFalse();
        await Assert.That(cut.FindComponents<MudSelect<int?>>().Any(item => item.Instance.Label == "Guest recovery")).IsFalse();

        await cut.InvokeAsync(() => handling.Instance.ValueChanged.InvokeAsync(3));
        await Assert.That(Select(cut, "Advance registration").Instance.Value).IsEqualTo(2);
        await Assert.That(Select(cut, "Advance registration").Instance.Disabled).IsFalse();
        await Assert.That(cut.FindComponents<MudSelect<int?>>().Any(item => item.Instance.Label == "Identity access")).IsFalse();

        await cut.InvokeAsync(() => handling.Instance.ValueChanged.InvokeAsync(4));
        var identity = Select(cut, "Identity access");
        await cut.InvokeAsync(() => identity.Instance.ValueChanged.InvokeAsync(3));
        var recovery = Select(cut, "Guest recovery");
        var recoveryValues = recovery.FindComponents<MudSelectItem<int?>>().Select(item => item.Instance.Value).ToArray();
        await Assert.That(recoveryValues).IsEquivalentTo(new int?[] { 4, 5 });
    }

    [Test]
    public async Task ExistingAccountRequired_WhenCapabilityDeniesIt_RemainsVisibleAndCannotBeSaved()
    {
        var cut = RenderEditor(
            CreateConfiguration(4, 2, 1, null),
            visitorAccess: new VisitorAccessCapabilityDto
            {
                AllowsNewNativeAllocation = true,
                AllowsAnonymousParticipation = true,
                AllowsAccountRequiredParticipation = false,
                AllowsExistingAccountLogin = true,
                SignupDestinations = []
            });

        IRenderedComponent<MudSelect<int?>> identity = Select(cut, "Identity access");
        var accountOption = identity.FindComponents<MudSelectItem<int?>>()
            .Single(item => item.Instance.Value == 1);

        await Assert.That(identity.Instance.Value).IsEqualTo(1);
        await Assert.That(accountOption.Instance.Disabled).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='account-required-unavailable'][role='note']")).Count().IsEqualTo(1);
        await Assert.That(cut.Find("button[data-testid='save-participation-configuration']").HasAttribute("disabled"))
            .IsTrue();
        await _eventService.DidNotReceiveWithAnyArgs().ConfigureEventParticipationAsync(
            default, default!, default, default);
    }

    [Test]
    public async Task Save_ForwardsCurrentConfigurationAndConcurrencyStamp()
    {
        var eventId = Guid.CreateVersion7();
        var configuration = CreateConfiguration(4, 3, 2, 1);
        var saved = false;
        _eventService.ConfigureEventParticipationAsync(
                eventId,
                Arg.Any<ConfigureEventParticipationDto>(),
                configuration.ConcurrencyStamp!.Value,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = eventId, Success = true });
        var cut = RenderEditor(configuration, eventId, () => saved = true);

        await cut.Find("button[data-testid='save-participation-configuration']")
            .ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("[data-testid='participation-save-status']")).Count().IsEqualTo(1);

        await _eventService.Received(1).ConfigureEventParticipationAsync(
            eventId,
            Arg.Is<ConfigureEventParticipationDto>(body =>
                body.ParticipationHandlingModeId == 4
                && body.AdvanceRegistrationObligationId == 3
                && body.IdentityAccessModeId == 2
                && body.GuestRecoveryPolicy == GuestRecoveryPolicyEnum.UnverifiedEmailAccepted),
            configuration.ConcurrencyStamp.Value,
            Arg.Any<CancellationToken>());
        await Assert.That(saved).IsTrue();
        await _announcer.Received(1).AnnouncePoliteAsync("Participation configuration saved.");
    }

    [Test]
    public async Task Save_WhenConcurrencyFails_RendersAndAnnouncesAccessibleError()
    {
        var eventId = Guid.CreateVersion7();
        var configuration = CreateConfiguration(4, 2, 1, null);
        _eventService.ConfigureEventParticipationAsync(
                eventId,
                Arg.Any<ConfigureEventParticipationDto>(),
                configuration.ConcurrencyStamp!.Value,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Participation configuration changed since it was loaded.",
                Errors = ["Refresh the event and try again."],
                FailureCode = "event_participation_configuration_concurrency_conflict"
            });
        var reloadRequested = false;
        var cut = RenderEditor(configuration, eventId, onReloadRequested: () => reloadRequested = true);

        await cut.Find("button[data-testid='save-participation-configuration']")
            .ClickAsync(new MouseEventArgs());
        var alert = cut.Find("[data-testid='participation-save-error'][role='alert']");

        await Assert.That(alert.TextContent).Contains("changed since it was loaded");
        await Assert.That(alert.TextContent).Contains("Refresh the event and try again");
        await Assert.That(reloadRequested).IsTrue();
        await _announcer.Received(1).AnnounceAssertiveAsync("Participation configuration changed since it was loaded.");
    }

    private IRenderedComponent<ParticipationConfigurationEditor> RenderEditor(
        ParticipationConfiguration configuration,
        Guid? eventId = null,
        Action? onSaved = null,
        VisitorAccessCapabilityDto? visitorAccess = null,
        Action? onReloadRequested = null) =>
        _ctx.RenderMudComponent<ParticipationConfigurationEditor>(parameters => parameters
            .Add(component => component.EventId, eventId ?? configuration.EventId!.Value)
            .Add(component => component.Configuration, configuration)
            .Add(component => component.VisitorAccess, visitorAccess ?? AccountRequiredAllowed())
            .Add(component => component.OnSaved, EventCallback.Factory.Create(this, onSaved ?? (() => { })))
            .Add(component => component.OnReloadRequested, EventCallback.Factory.Create(this, onReloadRequested ?? (() => { }))));

    private static VisitorAccessCapabilityDto AccountRequiredAllowed() => new()
    {
        AllowsNewNativeAllocation = true,
        AllowsAnonymousParticipation = true,
        AllowsAccountRequiredParticipation = true,
        AllowsExistingAccountLogin = true,
        SignupDestinations =
        [
            new VisitorSignupDestinationDto
            {
                Provider = AuthenticationProviderKind.Atproto,
                Url = "/login?provider=atproto"
            }
        ]
    };

    private static IRenderedComponent<MudSelect<int?>> Select(
        IRenderedComponent<ParticipationConfigurationEditor> cut,
        string label) => cut.FindComponents<MudSelect<int?>>().Single(item => item.Instance.Label == label);

    private static ParticipationConfiguration CreateConfiguration(
        int handling,
        int advance,
        int? identity,
        int? recovery)
    {
        var eventId = Guid.CreateVersion7();
        return new ParticipationConfiguration
        {
            EventId = eventId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            ParticipationHandlingModeId = handling,
            AdvanceRegistrationObligationId = advance,
            IdentityAccessModeId = identity,
            GuestRecoveryPolicy = recovery is { } value
                ? (GuestRecoveryPolicyEnum?)value
                : null
        };
    }
}
