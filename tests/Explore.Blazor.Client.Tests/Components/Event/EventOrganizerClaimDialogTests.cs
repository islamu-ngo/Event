using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventOrganizerClaimDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Submit_ValidEvidence_UsesActiveActorAndGeneratedClaimMethod()
    {
        var eventId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SubmitEventOrganizerClaimDto? captured = null;
        var claimService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventOrganizerClaimService>();
        claimService.SubmitEventOrganizerClaimAsync(
                eventId,
                Arg.Do<SubmitEventOrganizerClaimDto>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _ctx.Services.AddSingleton(claimService);
        var shellState = RegisterShellState();
        shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Organizer" }], actorId);
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        await dialogService.ShowAsync<EventOrganizerClaimDialog>(
            "Claim this event",
            new DialogParameters<EventOrganizerClaimDialog> { { dialog => dialog.EventId, eventId } });
        provider.WaitForState(() => provider.FindComponents<EventOrganizerClaimDialog>().Count == 1, TimeSpan.FromSeconds(3));
        var dialog = provider.FindComponent<EventOrganizerClaimDialog>();

        dialog.Find("input").Change(" website ");
        dialog.Find("textarea").Change(" https://organizer.test/about ");
        dialog.Find("form").Submit();
        dialog.WaitForState(() => captured is not null, TimeSpan.FromSeconds(3));

        await Assert.That(captured!.ClaimantActorId).IsEqualTo(actorId);
        await Assert.That(captured.EvidenceType).IsEqualTo("website");
        await Assert.That(captured.EvidenceReference).IsEqualTo("https://organizer.test/about");
    }

    public void Dispose() => _ctx.Dispose();

    private UiShellState RegisterShellState()
    {
        _ctx.Services.AddSingleton(provider => new UiShellState(
            provider.GetRequiredService<NavigationManager>(),
            new WorkspaceRouteClassifier(new WorkspaceRegistry())));
        return _ctx.Services.GetRequiredService<UiShellState>();
    }
}
