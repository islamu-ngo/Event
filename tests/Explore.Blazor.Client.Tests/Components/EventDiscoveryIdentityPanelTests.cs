using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Contracts.Services.Events;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class EventDiscoveryIdentityPanelTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IdentitySurface _service = new();
    private readonly IEventClient _events = Substitute.For<IEventClient>();
    private readonly List<Guid> _publicReads = [];

    public EventDiscoveryIdentityPanelTests()
    {
        _context.Services.AddSingleton<IEventDiscoveryIdentityService>(_service);
        _events.GetEventByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _publicReads.Add(call.Arg<Guid>());
                return PublicListing();
            });
        var management = Substitute.For<IEventManagementReadClient>();
        management.GetEventManagementDetailsAsync(Arg.Any<Guid>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(PublicListing());
        _context.Services.AddSingleton<IEventService>(provider => new EventService(
            _events,
            Substitute.For<IEventLifecycleClient>(),
            management,
            Substitute.For<IEventParticipationClient>(),
            Substitute.For<IEventPublicActionClient>(),
            provider.GetRequiredService<ILogger<EventService>>()));
    }

    private HalResourceOfEventDto PublicListing() => new()
    {
        Id = _service.TargetId, Slug = "Related public listing", PublicCode = "ABC123"
    };

    [Test]
    public async Task Refreshed_original_resource_discards_revoked_canonical_and_decision_links()
    {
        _service.Resource = Resource("canonical", "review");
        var original = Event();
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, original));
        await Assert.That(cut.FindAll("[data-identity-canonical]")).IsNotEmpty();
        _service.Resource = new() { EventId = _service.SourceId };
        cut.Render(parameters => parameters.Add(component => component.Event, original with { }));
        await Assert.That(cut.FindAll("[data-identity-canonical], [data-identity-same], [data-identity-reverse]"))
            .IsEmpty();
    }

    [Test]
    public async Task PayloadIdentifiersNeverGrantCanonicalOrDecisionAffordancesWithoutHal()
    {
        _service.Resource = new()
        {
            EventId = _service.SourceId, PublicPrimaryEventId = _service.TargetId,
            ReviewTargetEventId = _service.TargetId, ExpectedRevision = 7
        };
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, Event()));
        await Assert.That(cut.FindAll("[data-identity-canonical]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-identity-same], [data-identity-reverse], [data-identity-candidates]")).IsEmpty();
        await Assert.That(_publicReads).IsEmpty();
    }

    [Test]
    public async Task CanonicalGuidanceDoesNotNavigateOrChangeOriginalEvent()
    {
        _service.Resource = Resource("canonical");
        var original = Event();
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        string before = navigation.Uri;
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, original));
        await Assert.That(cut.Find("[data-identity-canonical]").GetAttribute("href"))
            .IsEqualTo("/events/related-public-listing-ABC123");
        await Assert.That(_publicReads.Single()).IsEqualTo(_service.TargetId);
        await Assert.That(navigation.Uri).IsEqualTo(before);
        await Assert.That(original.Id).IsEqualTo(_service.SourceId);
        await Assert.That(_service.Decisions).IsEmpty();
    }

    [Test]
    [Arguments(404)]
    [Arguments(403)]
    [Arguments(503)]
    public async Task Unavailable_public_canonical_does_not_use_management_detail(int status)
    {
        _service.Resource = Resource("canonical", "review");
        _events.GetEventByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("unavailable", status, "", new Dictionary<string, IEnumerable<string>>(), null));
        var original = Event();
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, original));
        await Assert.That(cut.FindAll("[data-identity-canonical]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-identity-same]")).IsNotEmpty();
        await Assert.That(original.Id).IsEqualTo(_service.SourceId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_public_resource_or_public_code_withholds_canonical_href(bool missingCode)
    {
        _service.Resource = Resource("canonical");
        _events.GetEventByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(missingCode ? new HalResourceOfEventDto { Id = _service.TargetId, Slug = "Related" } : null!);
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, Event()));
        await Assert.That(cut.FindAll("[data-identity-canonical]")).IsEmpty();
    }

    [Test]
    [Arguments("same")]
    [Arguments("different")]
    [Arguments("reverse")]
    public async Task DecisionRemainsBoundToOriginalEventAndExplicitCurrentTarget(string button)
    {
        _service.Resource = Resource("review", "reverse");
        _service.Resource._links!["canonical"] = new() { Href = $"/api/event/{_service.TargetId}", Method = "GET" };
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, Event()));
        await cut.Find("input[type=checkbox]").ChangeAsync(new() { Value = true });
        await cut.Find($"[data-identity-{button}]").ClickAsync(new());
        var decision = _service.Decisions.Single();
        await Assert.That(cut.Find("[data-identity-canonical]").GetAttribute("href"))
            .IsEqualTo("/events/related-public-listing-ABC123");
        await Assert.That(decision.EventId).IsEqualTo(_service.SourceId);
        await Assert.That(decision.Request.PrimaryEventId).IsEqualTo(_service.TargetId);
        await Assert.That(decision.Request.ExpectedRevision).IsEqualTo(7);
        await Assert.That(decision.Request.Decision).IsEqualTo(button switch
        {
            "same" => "same-offering", "different" => "different-offering", _ => "reverse"
        });
        await Assert.That(cut.FindAll("[data-identity-reload]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task StaleRevisionRetainsInputAndRequiresReloadAndFreshConfirmation()
    {
        _service.Resource = Resource("review", "candidates");
        _service.RejectNextDecision = true;
        var cut = _context.RenderMudComponent<EventDiscoveryIdentityPanel>(parameters =>
            parameters.Add(component => component.Event, Event()));
        await cut.Find("[data-identity-candidates]").ClickAsync(new());
        await cut.Find("select[id$='-candidate']").ChangeAsync(new() { Value = _service.TargetId.ToString() });
        await cut.Find("select[id$='-reason']").ChangeAsync(new() { Value = "separate_programs" });
        await cut.Find("input[type=checkbox]").ChangeAsync(new() { Value = true });
        await cut.Find("[data-identity-different]").ClickAsync(new());
        await Assert.That(_service.Decisions.Count).IsEqualTo(1);
        await Assert.That(cut.Find("fieldset").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("select[id$='-reason']").GetAttribute("value")
            ?? cut.Find("select[id$='-reason'] option[selected]").GetAttribute("value")).IsEqualTo("separate_programs");

        _service.Resource = Resource("review", "candidates", revision: 8);
        await cut.Find("[data-identity-reload]").ClickAsync(new());
        await Assert.That(_service.LastCandidate).IsEqualTo(_service.TargetId);
        await Assert.That(cut.Find("[data-identity-different]").HasAttribute("disabled")).IsTrue();
        await Assert.That(_service.Decisions.Count).IsEqualTo(1);
        await cut.Find("input[type=checkbox]").ChangeAsync(new() { Value = true });
        await cut.Find("[data-identity-different]").ClickAsync(new());
        await Assert.That(_service.Decisions.Last().Request.ExpectedRevision).IsEqualTo(8);
        await Assert.That(_service.Decisions.Last().Request.ReasonCode).IsEqualTo("separate_programs");
    }

    private EventDto Event()
    {
        var result = new EventDto { Id = _service.SourceId, Title = "Original event" };
        result.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["discovery-identity"] = new() { Href = $"/api/event/{_service.SourceId}/discovery-identity", Method = "GET" }
            });
        return result;
    }

    private HalResourceOfEventDiscoveryIdentityDto Resource(string first, string? second = null, long revision = 7) =>
        HalLinkTestFactory.WithLinks(new HalResourceOfEventDiscoveryIdentityDto
        {
            EventId = _service.SourceId, PublicPrimaryEventId = _service.TargetId,
            ReviewTargetEventId = _service.TargetId, ExpectedRevision = revision
        }, new[] { first, second }.OfType<string>(),
            $"/api/event/{_service.SourceId}/discovery-identity/review", "POST");

    private sealed class IdentitySurface : IEventDiscoveryIdentityService
    {
        public Guid SourceId { get; } = Guid.CreateVersion7();
        public Guid TargetId { get; } = Guid.CreateVersion7();
        public HalResourceOfEventDiscoveryIdentityDto Resource { get; set; } = new();
        public List<(Guid EventId, ReviewEventDiscoveryAliasDto Request)> Decisions { get; } = [];
        public Guid? LastCandidate { get; private set; }
        public bool RejectNextDecision { get; set; }

        public Task<HalResourceOfEventDiscoveryIdentityDto> GetAsync(
            Guid eventId, Guid? candidateEventId = null, CancellationToken cancellationToken = default)
        {
            LastCandidate = candidateEventId;
            return Task.FromResult(Resource);
        }

        public Task<HalResourceOfEventDuplicateCandidatesDto> GetCandidatesAsync(
            Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HalResourceOfEventDuplicateCandidatesDto
            {
                EventId = SourceId, ExpectedRevision = 7,
                Candidates = [new EventDuplicateCandidateDto { Id = TargetId, Title = "Public candidate" }]
            });

        public Task<BaseCommandResponseOfGuid> ReviewAsync(
            Guid eventId, ReviewEventDiscoveryAliasDto decision, CancellationToken cancellationToken = default)
        {
            Decisions.Add((eventId, decision));
            if (RejectNextDecision)
            {
                RejectNextDecision = false;
                throw new ApiException("stale", 409, "", new Dictionary<string, IEnumerable<string>>(), null);
            }
            return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
