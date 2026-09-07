using Explore.Blazor.Client.Pages.Events.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class ManageSpeakersDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventSessionSpeakerService _speakerService = Substitute.For<IEventSessionSpeakerService>();
    private readonly IActorService _actorService = Substitute.For<IActorService>();

    public ManageSpeakersDialogTests()
    {
        _ctx.Services.AddSingleton(_speakerService);
        _ctx.Services.AddSingleton(_actorService);
        _actorService.GetActorsAsync().Returns(Task.FromResult<ICollection<ActorListDto>>(new List<ActorListDto>()));
    }

    [Test]
    public async Task Render_WhenHalLinksExist_ShowsCreateAndDeleteControls()
    {
        var sessionId = Guid.NewGuid();
        _speakerService.GetSpeakersBySessionAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSpeakerCollection(canCreate: true, canDelete: true)));

        var markup = RenderDialogMarkup(sessionId);

        await Assert.That(markup).Contains("Add Speaker");
        await Assert.That(markup).Contains("aria-label=\"Remove speaker\"");
    }

    [Test]
    public async Task Render_WhenHalLinksAreMissing_HidesCreateAndDeleteControls()
    {
        var sessionId = Guid.NewGuid();
        _speakerService.GetSpeakersBySessionAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateSpeakerCollection(canCreate: false, canDelete: false)));

        var markup = RenderDialogMarkup(sessionId);

        await Assert.That(markup).DoesNotContain("Add Speaker");
        await Assert.That(markup).DoesNotContain("aria-label=\"Remove speaker\"");
    }

    public void Dispose() => _ctx.Dispose();

    private string RenderDialogMarkup(Guid sessionId)
    {
        _ctx.Render<MudPopoverProvider>();
        var provider = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<ManageSpeakersDialog>
        {
            { component => component.SessionId, sessionId }
        };

        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = dialogService.ShowAsync<ManageSpeakersDialog>("Manage Speakers", parameters);

        provider.WaitForState(() => provider.Markup.Contains("Speaker One", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        return provider.Markup;
    }

    private static HalCollectionResourceOfEventSessionSpeakerListDto CreateSpeakerCollection(bool canCreate, bool canDelete)
    {
        var speakerId = Guid.NewGuid();
        var embeddedItem = new HalResourceOfEventSessionSpeakerListDto
        {
            Id = speakerId,
            ActorDisplayName = "Speaker One"
        };

        if (canDelete)
        {
            embeddedItem = HalLinkTestFactory.WithLinks(
                embeddedItem,
                new HalLinkTestLink("delete", "/api/eventsessionspeaker/management/by-session/session/speaker"));
        }

        return new HalCollectionResourceOfEventSessionSpeakerListDto
        {
            _links = canCreate
                ? new Dictionary<string, HalLink>
                {
                    ["create"] = new() { Href = "/api/eventsessionspeaker/management/by-session/session" }
                }
                : null,
            _embedded = new HalCollectionEmbeddedOfEventSessionSpeakerListDto
            {
                Items = new List<HalResourceOfEventSessionSpeakerListDto> { embeddedItem }
            }
        };
    }
}
