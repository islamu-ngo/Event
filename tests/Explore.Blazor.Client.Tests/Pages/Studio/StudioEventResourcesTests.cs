using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

/// <summary>Collection authority and per-resource authority are separate HAL decisions.</summary>
public sealed class StudioEventResourcesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventResourceManagementClient _management;
    private readonly IEventResourceExportClient _export;
    private readonly IAccessibilityFocusService _focus;
    private readonly Guid _eventId = Guid.CreateVersion7();
    private readonly Guid _resourceId = Guid.CreateVersion7();
    private readonly Guid _version = Guid.CreateVersion7();

    public StudioEventResourcesTests()
    {
        _management = _ctx.AddMockService<IEventResourceManagementClient>();
        _export = _ctx.AddMockService<IEventResourceExportClient>();
        _focus = _ctx.AddMockService<IAccessibilityFocusService>();
        _ctx.Services.AddScoped<IEventResourceService>(_ => new EventResourceService(
            Substitute.For<IEventResourcesClient>(), _management, _export));
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("create-resource", "export")]
    [Arguments("export", "create-resource")]
    public async Task CollectionActionsRequireExactCollectionRelation(string denied, string unrelated)
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection([unrelated]));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("self"));
        var cut = Render();

        cut.WaitForElement("[data-testid='studio-resource-title']");
        await Assert.That(cut.FindAll($"[data-testid='resource-{denied}']")).IsEmpty();
        await _export.DidNotReceive().ExportEventResourceMetadataAsync(Arg.Any<Guid>(), Arg.Any<int?>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("edit", "delete")]
    [Arguments("publish", "edit")]
    [Arguments("unpublish", "publish")]
    [Arguments("archive", "publish")]
    [Arguments("delete", "edit")]
    [Arguments("view-audit", "edit")]
    [Arguments("moderate", "unpublish")]
    public async Task ItemActionsRequireExactDetailRelationDespiteOtherActions(string denied, string unrelated)
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection(["create-resource", "export"], unrelated));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item(unrelated));
        var cut = Render();

        cut.WaitForElement("[data-testid='studio-resource-title']");
        await Assert.That(cut.FindAll($"[data-testid='resource-{denied}']")).IsEmpty();
    }

    [Test]
    [Arguments("edit")]
    [Arguments("publish")]
    [Arguments("unpublish")]
    [Arguments("archive")]
    [Arguments("delete")]
    [Arguments("view-audit")]
    [Arguments("moderate")]
    public async Task ExactDetailRelationMakesItsActionAvailable(string relation)
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection([], relation));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item(relation));

        var cut = Render();
        var action = cut.WaitForElement($"[data-testid='resource-{relation}']");
        await Assert.That(action).IsNotNull();
    }

    [Test]
    public async Task StalePublishDenialRefreshesAndRemovesTheAction()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection([], "publish"), Collection([], "self"));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("publish"), Item("publish"), Item("self"));
        _management.PublishEventResourceAsync(_resourceId, Arg.Any<string>(), Arg.Any<EventResourceVersionRequestDto>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new ApiException("Forbidden", 403, "private policy detail",
                new Dictionary<string, IEnumerable<string>>(), null));
        var cut = Render();
        var publish = cut.WaitForElement("[data-testid='resource-publish']");
        publish.Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='resource-publish']")).IsEmpty());
        await _management.Received(1).PublishEventResourceAsync(_resourceId, Arg.Any<string>(),
            Arg.Is<EventResourceVersionRequestDto>((EventResourceVersionRequestDto request) => request.ExpectedVersion == _version),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("private policy detail");
    }

    [Test]
    public async Task DeleteRequiresASeparateConfirmationBeforeAnyMutation()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection([], "delete"));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("delete"));

        var cut = Render();
        cut.WaitForElement("[data-testid='resource-delete']").Click();

        await Assert.That(cut.FindAll("[data-testid='resource-confirm-delete']")).HasSingleItem();
        await _management.DidNotReceive().DeleteEventResourceAsync(Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<EventResourceVersionRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateFormRequiresAudienceAndLabelsItsWriteOnlyDestinationSeparately()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Collection(["create-resource"], "configure-destination"));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("configure-destination"));
        var cut = Render();
        cut.WaitForElement("[data-testid='resource-create-resource']").Click();
        await Assert.That(cut.Find("label[for='resource-audience']").TextContent).IsNotEmpty();
        await Assert.That(cut.Find("#resource-audience").HasAttribute("required")).IsTrue();
        cut.Find("[data-testid='resource-configure-destination']").Click();
        await Assert.That(cut.Find("label[for='resource-destination']").TextContent).IsNotEmpty();
        await Assert.That(cut.Find("#resource-destination").GetAttribute("autocomplete")).IsEqualTo("off");
        await Assert.That(cut.FindAll("[value*='destination.example.test']")).IsEmpty();
    }

    [Test]
    public async Task CancellingEditorRestoresFocusToItsOriginalAction()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection(["create-resource"]));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item());

        var cut = Render();
        await cut.WaitForElement("[data-testid='resource-create-resource']").ClickAsync();
        await _focus.Received(1).SaveFocusAsync();
        await cut.Find("form button[type='button']").ClickAsync();

        await Assert.That(cut.FindAll("form")).IsEmpty();
        await Assert.That(cut.Find("#resource-studio-focus")).IsNotNull();
        await _focus.Received(1).RestoreFocusAsync("#resource-studio-focus");
    }

    [Test]
    public async Task InvalidTitleIsAssociatedWithItsFormControl()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection(["create-resource"]));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item());

        var cut = Render();
        await cut.WaitForElement("[data-testid='resource-create-resource']").ClickAsync();
        await cut.Find("form").SubmitAsync();

        await Assert.That(cut.Find("#resource-title").GetAttribute("aria-describedby"))
            .IsEqualTo("resource-title-error");
        await Assert.That(cut.Find("#resource-title-error").TextContent.Trim()).IsNotEmpty();
        await _management.DidNotReceive().CreateEventResourceAsync(Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CreateEventResourceRequestDto>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FailedSaveRefocusesTheEditorAfterItsResourceListReloads()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection(["create-resource"]));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item());
        _management.CreateEventResourceAsync(_eventId, Arg.Any<string>(),
            Arg.Any<CreateEventResourceRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BaseCommandResponseOfGuid>(new ApiException("Denied", 403, "",
                new Dictionary<string, IEnumerable<string>>(), null)));

        var cut = Render();
        await cut.WaitForElement("[data-testid='resource-create-resource']").ClickAsync();
        await cut.Find("#resource-title").ChangeAsync("Handout");
        await cut.Find("#resource-audience").ChangeAsync("Public");
        await cut.Find("form").SubmitAsync();

        await Assert.That(cut.Find("#resource-title")).IsNotNull();
        await _focus.Received(1).FocusAsync("#resource-title", Arg.Any<bool>());
    }

    [Test]
    public async Task ExportRequiresCollectionLinkAndRendersAuthorizedMetadata()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Collection(["export"]));
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("self"));
        _export.ExportEventResourceMetadataAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EventResourceMetadataExportPageDto { EventId = _eventId,
                Items = [new EventResourceMetadataExportDto { Id = _resourceId, Title = "Authorized handout" }] });
        var cut = Render();
        cut.WaitForElement("[data-testid='resource-export']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Authorized handout"));
        await _export.Received(1).ExportEventResourceMetadataAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ChangedVersionDeniesPublishWithoutSubmittingAndRefreshes()
    {
        _management.ListEventResourceManagementAsync(_eventId, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Collection([], "publish"), Collection([], "self"));
        var changed = Item("publish");
        changed.Version = Guid.CreateVersion7();
        _management.GetEventResourceManagementDetailAsync(_resourceId, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(Item("publish"), changed, Item("self"));

        var cut = Render();
        cut.WaitForElement("[data-testid='resource-publish']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='resource-publish']")).IsEmpty());
        await _management.DidNotReceive().PublishEventResourceAsync(Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<EventResourceVersionRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<DynamicComponent> Render()
    {
        var type = typeof(IEventResourceManagementClient).Assembly.GetType("Explore.Blazor.Client.Pages.Studio.StudioEventResources");
        if (type is null) throw new InvalidOperationException("StudioEventResources component is absent");
        return _ctx.RenderMudComponent<DynamicComponent>(parameters => parameters
            .Add(component => component.Type, type)
            .Add(component => component.Parameters, new Dictionary<string, object?> { ["EventId"] = _eventId }));
    }

    private EventResourceManagementCollectionDto Collection(string[] collectionLinks, params string[] itemLinks) => new()
    {
        PageNumber = 1, PageSize = 20,
        _links = Links(collectionLinks),
        _embedded = new HalCollectionEmbeddedOfEventResourceManagementDto { Items = [Item(itemLinks)] }
    };

    private HalResourceOfEventResourceManagementDto Item(params string[] links) => new()
    {
        Id = _resourceId, EventId = _eventId, Version = _version,
        Draft = new EventResourceDraftDto { Title = "Organizer handout" },
        _links = Links(links)
    };

    private Dictionary<string, HalLink> Links(params string[] relations) => relations.ToDictionary(
        relation => relation,
        relation => new HalLink { Href = $"/api/event/resources/{_resourceId}/{relation}",
            Method = relation is "edit" ? "PUT" : "POST" });
}
