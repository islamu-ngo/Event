using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.Client.Tests.Components.Event;

/// <summary>Contract for the attendee surface: HAL, not identity or metadata, grants an action.</summary>
public sealed class EventResourcesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventResourcesClient _client;
    private readonly Guid _eventId = Guid.CreateVersion7();
    private readonly Guid _resourceId = Guid.CreateVersion7();
    private readonly Guid _alternativeId = Guid.CreateVersion7();

    public EventResourcesTests()
    {
        _client = _ctx.AddMockService<IEventResourcesClient>();
        _ctx.Services.AddScoped<IEventResourceService>(_ => new EventResourceService(_client,
            Substitute.For<IEventResourceManagementClient>(), Substitute.For<IEventResourceExportClient>()));
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("download", "access")]
    [Arguments("access", "download")]
    public async Task DeliveryActionRequiresItsOwnDetailRelation(string action, string unrelated)
    {
        var detail = Detail(unrelated);
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='event-resource-title']").Count).IsEqualTo(1));
        await Assert.That(cut.FindAll($"[data-testid='resource-{action}']")).IsEmpty();
    }

    [Test]
    [Arguments("download")]
    [Arguments("access")]
    public async Task AdvertisedDeliveryUsesOnlySameOriginHalHref(string relation)
    {
        var detail = Detail(relation);
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        var link = cut.WaitForElement($"[data-testid='resource-{relation}']");
        await Assert.That(link.GetAttribute("href")).IsEqualTo($"/api/event/resources/{_resourceId}/{relation}");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StudioNavigationRequiresManageResourcesCollectionRelation(bool advertised)
    {
        var page = Page(Detail("download"));
        if (advertised)
            page = page with { _links = new Dictionary<string, HalLink>(page._links)
            {
                ["manage-resources"] = new() { Href = $"/studio/events/{_eventId}/resources", Method = "GET" }
            } };
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(page);
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Detail("download"));
        var cut = Render();
        cut.WaitForElement("[data-testid='event-resource-title']");
        if (!advertised)
            await Assert.That(cut.FindAll("[data-testid='resource-manage-resources']")).IsEmpty();
        else
            await Assert.That(cut.Find("[data-testid='resource-manage-resources']").GetAttribute("href"))
                .IsEqualTo($"/studio/events/{_eventId}/resources");
    }

    [Test]
    public async Task SafeOriginIsTextNotAReadOrPreviewDestination()
    {
        var detail = Detail("access");
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        cut.WaitForElement("[data-testid='resource-access']");
        await Assert.That(cut.FindAll("iframe,embed,object,video,audio,link[rel='preload'],link[rel='prefetch']")).IsEmpty();
        await Assert.That(cut.FindAll("[src*='destination.example.test'],[href*='destination.example.test']")).IsEmpty();
        await _client.DidNotReceive().GetEventResourceAccessAsync(Arg.Any<Guid>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExternalAccessAnnouncesTheOutsideServiceBeforeFollowingItsAction()
    {
        var detail = Detail("access");
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        var access = cut.WaitForElement("[data-testid='resource-access']");
        var warning = cut.Find($"#resource-external-warning-{_resourceId:N}");
        await Assert.That(warning.TextContent.Trim()).IsNotEmpty();
        await Assert.That(access.GetAttribute("aria-describedby")).IsEqualTo(warning.Id);
        await Assert.That(warning.NextElementSibling?.GetAttribute("data-testid")).IsEqualTo("resource-access");
        await Assert.That(access.GetAttribute("href")).IsEqualTo($"/api/event/resources/{_resourceId}/access");
    }

    [Test]
    public async Task DownloadDetailsExposeTheValidatedFileType()
    {
        var detail = Detail("download");
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        cut.WaitForElement("[data-testid='resource-download']");
        await Assert.That(cut.Markup).Contains(detail.File!.ContentType);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AccessibleAlternativeRequiresItsOwnRelation(bool advertised)
    {
        var detail = advertised ? Detail("accessible-alternative") : Detail();
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);

        var cut = Render();
        cut.WaitForElement("[data-testid='event-resource-title']");
        if (advertised)
            await Assert.That(cut.Find("[data-testid='resource-accessible-alternative']").GetAttribute("href"))
                .IsEqualTo($"/api/event/resources/{_resourceId}/accessible-alternative");
        else
            await Assert.That(cut.FindAll("[data-testid='resource-accessible-alternative']")).IsEmpty();
    }

    [Test]
    public async Task CrossOriginDeliveryHrefIsNotRendered()
    {
        var detail = Detail("access");
        detail._links!["access"].Href = "https://destination.example.test/private";
        _client.ListEventResourcesAsync(_eventId, Arg.Any<int?>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Page(detail));
        _client.GetEventResourceAudienceDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(detail);
        var cut = Render();
        cut.WaitForElement("[data-testid='event-resource-title']");
        await Assert.That(cut.FindAll("[data-testid='resource-access']")).IsEmpty();
        await Assert.That(cut.FindAll("[href*='destination.example.test']")).IsEmpty();
    }

    private IRenderedComponent<DynamicComponent> Render()
    {
        var type = typeof(IEventResourcesClient).Assembly.GetType("Explore.Blazor.Client.Components.Event.EventResources");
        if (type is null) throw new InvalidOperationException("P8.1 Red: EventResources attendee component is absent");
        return _ctx.RenderMudComponent<DynamicComponent>(parameters => parameters
            .Add(component => component.Type, type)
            .Add(component => component.Parameters, new Dictionary<string, object?> { ["EventId"] = _eventId }));
    }

    private HalResourceOfEventResourceAudienceDetailDto Detail(params string[] relations) => new()
    {
        Id = _resourceId, EventId = _eventId, Title = "Accessible handout", Availability = "available",
        Requirements = "Admission required", LanguageCode = "en", IsTeaser = false,
        File = relations.Contains("download")
            ? new EventResourceFileMetadataDto { FileName = "handout.pdf", SizeBytes = 1024,
                ContentType = "application/pdf", SafetyState = "Ready" }
            : null,
        ExternalDestinationSafeOrigin = relations.Contains("access") ? "https://destination.example.test" : null,
        AccessibleAlternativeEventResourceId = _alternativeId,
        _links = relations.ToDictionary(relation => relation,
            relation => new HalLink { Href = $"/api/event/resources/{_resourceId}/{relation}", Method = "GET" })
    };

    private static EventResourceAudiencePageResource Page(HalResourceOfEventResourceAudienceDetailDto item) => new()
    {
        _links = new Dictionary<string, HalLink> { ["self"] = new() { Href = "/api/event/resources" } },
        _embedded = new HalCollectionEmbeddedOfEventResourceAudienceDetailDto { Items = [item] }
    };
}
