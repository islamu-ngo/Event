using System.Text.Json;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventProvenancePanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public EventProvenancePanelTests()
    {
        _ctx.Services.AddSingleton(provider => new UiShellState(
            provider.GetRequiredService<NavigationManager>(),
            new WorkspaceRouteClassifier(new WorkspaceRegistry())));
        _ctx.Services.AddSingleton(Substitute.For<IEventService>());
        _ctx.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventOrganizerClaimService>());
    }

    [Test]
    public async Task Render_WithoutActionLinks_ShowsFactsButHidesActions()
    {
        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(parameters => parameters
            .Add(component => component.Event, CreateCommunityEvent()));

        await Assert.That(cut.Markup).Contains("Community reported");
        await Assert.That(cut.Markup).Contains("Community Calendar");
        await Assert.That(cut.Markup).DoesNotContain("Suggest a correction");
        await Assert.That(cut.Markup).DoesNotContain("Claim this event");
        await Assert.That(cut.Markup).DoesNotContain("Report unsafe link");
    }

    [Test]
    public async Task Render_WithSafeSourceLink_UsesOnlyHalRedirectInNewTab()
    {
        var eventDto = CreateCommunityEvent("source");

        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(parameters => parameters
            .Add(component => component.Event, eventDto));

        var source = cut.Find("a.event-provenance-panel__source-link");
        await Assert.That(source.GetAttribute("href")).IsEqualTo("/api/events/source");
        await Assert.That(source.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(source.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
    }

    [Test]
    public async Task Render_WithUnsafeAbsoluteSourceHalLink_HidesSourceLink()
    {
        var eventDto = CreateCommunityEvent();
        eventDto = eventDto with { AdditionalProperties = CreateHalLinks(("source", "https://attacker.test/event")) };

        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(parameters => parameters
            .Add(component => component.Event, eventDto));

        await Assert.That(cut.FindAll("a.event-provenance-panel__source-link")).IsEmpty();
    }

    [Test]
    public async Task Render_ClaimantCollection_FiltersEventAndUsesItemWithdrawLinkOnly()
    {
        var actorId = Guid.NewGuid();
        var eventDto = CreateCommunityEvent();
        var claim = new HalResourceOfEventOrganizerClaimDto
        {
            Id = Guid.NewGuid(),
            EventId = eventDto.Id,
            ClaimantActorId = Guid.NewGuid(),
            StatusName = "Pending review",
            EvidenceType = "website",
            EvidenceReference = "https://organizer.test/about",
            _links = new Dictionary<string, HalLink> { ["withdraw-claim"] = new() { Href = "/api/events/claim/withdraw", Method = "POST" } }
        };
        var unrelatedClaim = new HalResourceOfEventOrganizerClaimDto
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            ClaimantActorId = actorId,
            StatusName = "Unrelated claim",
            EvidenceType = "website",
            EvidenceReference = "https://other.test",
            _links = new Dictionary<string, HalLink> { ["withdraw-claim"] = new() { Href = "/api/events/other/withdraw", Method = "POST" } }
        };
        var claimService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventOrganizerClaimService>();
        claimService.GetClaimantOrganizerClaimsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns([claim, unrelatedClaim]);
        _ctx.Services.RemoveAll<Explore.Blazor.Client.Contracts.Services.IEventOrganizerClaimService>();
        _ctx.Services.AddSingleton(claimService);
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Organizer" }], actorId);

        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(parameters => parameters
            .Add(component => component.Event, eventDto));
        cut.WaitForState(() => cut.Markup.Contains("Pending review", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Withdraw claim");
        await Assert.That(cut.Markup).DoesNotContain("Unrelated claim");
    }

    [Test]
    public async Task Render_PublicActions_UsesSafeItemHalRedirectAndShowsDestinationDomain()
    {
        var eventDto = CreateCommunityEvent("public-actions");
        var action = new HalResourceOfEventPublicActionDto
        {
            Id = Guid.NewGuid(),
            EventId = eventDto.Id,
            Label = "Registration",
            DestinationDomain = "registration.example",
            Url = "https://untrusted-raw.example/register",
            _links = new Dictionary<string, HalLink>
            {
                ["external-registration"] = new() { Href = "/api/events/actions/redirect", Method = "GET" }
            }
        };
        var eventService = Substitute.For<IEventService>();
        eventService.GetEventPublicActionsAsync(eventDto.Id!.Value, Arg.Any<CancellationToken>())
            .Returns([action]);
        _ctx.Services.RemoveAll<IEventService>();
        _ctx.Services.AddSingleton(eventService);

        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(parameters => parameters
            .Add(component => component.Event, eventDto));
        cut.WaitForState(() => cut.Markup.Contains("registration.example", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        var link = cut.Find("a.event-provenance-panel__public-action");
        await Assert.That(link.GetAttribute("href")).IsEqualTo("/api/events/actions/redirect");
        await Assert.That(link.GetAttribute("href")).IsNotEqualTo(action.Url);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
    }

    public void Dispose() => _ctx.Dispose();

    private static EventDto CreateCommunityEvent(params string[] links) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Community Program",
        ProvenanceTypeCode = "COMMUNITY_REPORTED",
        ProvenanceTypeName = "Community reported",
        SourcePublisherName = "Community Calendar",
        AdditionalProperties = CreateHalLinks(links.Select(link => (link, $"/api/events/{link}")).ToArray())
    };

    private static Dictionary<string, object> CreateHalLinks(params (string Rel, string Href)[] links)
    {
        var json = JsonSerializer.Serialize(links.ToDictionary(
            link => link.Rel,
            link => new HalLink { Href = link.Href, Method = "GET" }));
        using var document = JsonDocument.Parse(json);
        return new Dictionary<string, object> { ["_links"] = document.RootElement.Clone() };
    }
}
