using System.Net;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventPromotionsTests : IDisposable
{
    private static readonly Guid CatalogVersionId = Guid.Parse("019fcf29-f8d4-7d83-94c5-08ba1e7f1201");
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventPromotionService _promotions;
    private readonly IEventTicketingService _ticketing;
    private readonly IAccessibilityAnnouncerService _announcer;
    private readonly IAccessibilityFocusService _focus;

    public StudioEventPromotionsTests()
    {
        _promotions = _ctx.AddMockService<IEventPromotionService>();
        _ticketing = _ctx.AddMockService<IEventTicketingService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
        _focus = _ctx.AddMockService<IAccessibilityFocusService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task CatalogOrCollectionUnavailable_FailsClosed()
    {
        var eventId = Guid.CreateVersion7();
        _ticketing.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns((EventTicketCatalogState?)null);

        var cut = Render(eventId);

        cut.WaitForAssertion(() => cut.Markup.Contains("Promotion management is not available", StringComparison.Ordinal));
        await Assert.That(cut.FindAll("[data-testid='show-create-promotion']")).IsEmpty();
        await _promotions.DidNotReceive().GetPromotionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EmptyCollection_ShowsCreateOnlyFromCreatePromotionRelation()
    {
        var eventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [], "create-promotion"));

        var cut = Render(eventId);

        cut.WaitForElement("[data-testid='show-create-promotion']");
        await Assert.That(cut.Markup).Contains("No promotions yet.");
        cut.Find("[data-testid='show-create-promotion']").Click();
        await Assert.That(cut.FindAll("[data-testid='promotion-definition-form']").Count).IsEqualTo(1);
        await _announcer.Received(1).AnnouncePoliteAsync("Promotion management loaded: 0 promotions.");
    }

    [Test]
    public async Task RelationAbsence_HidesMutationsAndKeepsMaskedReadState()
    {
        var eventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [Promotion("Published launch", "Published", "SAVE-••24")]));

        var cut = Render(eventId);

        cut.WaitForElement("[data-testid='promotion-card']");
        await Assert.That(cut.Markup).Contains("Published launch");
        await Assert.That(cut.Markup).Contains("SAVE-••24");
        await Assert.That(cut.FindAll("[data-testid='show-create-promotion']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='revise-promotion']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='publish-promotion']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='revoke-promotion']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='rotate-promotion-code']")).IsEmpty();
    }

    [Test]
    public async Task CreateDraft_SubmitsCompleteRequestAndRemovesIssuedCodeAfterDismissal()
    {
        var eventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        var draft = Promotion("Launch", "Draft", null, "publish");
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [], "create-promotion"), Collection(eventId, [draft], "create-promotion"));
        _promotions.CreateDraftAsync(eventId, Arg.Any<CreatePromotionDraftRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionCodeIssuedCommandResponseDto { Success = true, IssuedCode = "LAUNCH10" });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='show-create-promotion']").Click();
        cut.Find("[data-testid='promotion-display-label-input']").Change("Launch");
        cut.Find("[data-testid='promotion-code-input']").Change("LAUNCH10");
        cut.Find("[data-testid='save-promotion-definition']").Click();
        cut.WaitForElement("[data-testid='promotion-issued-code']");

        await _promotions.Received(1).CreateDraftAsync(
            eventId,
            Arg.Is<CreatePromotionDraftRequest>(request =>
                request.TicketCatalogVersionId == CatalogVersionId
                && request.DisplayLabel == "Launch"
                && request.Code == "LAUNCH10"
                && request.DiscountKind == "fixed"
                && request.FixedDiscountMinor == 100
                && request.StartsAtUtc.Offset == TimeSpan.Zero
                && request.EndsAtUtc > request.StartsAtUtc
                && request.EligibleTicketTypeIds.Count == 0),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).Contains("LAUNCH10");
        cut.Find("[data-testid='dismiss-issued-code']").Click();
        await Assert.That(cut.Markup).DoesNotContain("LAUNCH10");
        await _announcer.Received(1).AnnouncePoliteAsync("Promotion draft created. Copy the code now; it will not be shown again.");
        await _focus.Received(1).FocusByIdAsync("studio-promotion-feedback", true);
    }

    [Test]
    public async Task Revision_PreservesDefinitionShapeAndUsesExactRelation()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        var published = Promotion(
            "Launch",
            "Published",
            "SAVE-••24",
            ["revise-promotion"],
            includesAllTickets: false,
            eligibleTicketTypeIds: [ticketTypeId]);
        var revised = published with { DisplayLabel = "Revised launch", StatusName = "Draft", Links = Links("publish") };
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [published]), Collection(eventId, [revised]));
        _promotions.ReviseAsync(eventId, published.DefinitionId, Arg.Any<RevisePromotionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionManagementCommandResponseDto { Success = true });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='revise-promotion']").Click();
        cut.Find("[data-testid='promotion-display-label-input']").Change("Revised launch");
        cut.Find("[data-testid='save-promotion-definition']").Click();
        cut.WaitForElement("[data-testid='promotion-status']");

        await _promotions.Received(1).ReviseAsync(
            eventId,
            published.DefinitionId,
            Arg.Is<RevisePromotionRequest>(request =>
                request.DisplayLabel == "Revised launch"
                && request.DiscountKind == published.DiscountKind
                && request.FixedDiscountMinor == published.FixedDiscountMinor
                && request.EligibleTicketTypeIds.SequenceEqual(new[] { ticketTypeId })),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).Contains("Promotion revision created.");
        await _focus.Received().FocusByIdAsync("studio-promotion-feedback", true);
    }

    [Test]
    public async Task PublishAndRotate_UseExactRelationsAndKeepOnlyRotatedCodeTransient()
    {
        var eventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        var draft = Promotion("Launch", "Draft", null, "publish");
        var published = draft with { StatusName = "Published", PromotionCodeDisplayLabel = "LAUNCH-••10", Links = Links("rotate-promotion-code", "revoke") };
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [draft]), Collection(eventId, [published]), Collection(eventId, [published]));
        _promotions.PublishAsync(eventId, draft.DefinitionId, Arg.Any<PromotionCodeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionManagementCommandResponseDto { Success = true });
        _promotions.RotateCodeAsync(eventId, draft.DefinitionId, Arg.Any<PromotionCodeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionCodeIssuedCommandResponseDto { Success = true, IssuedCode = "ROTATE20" });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='publish-promotion']").Click();
        cut.Find("[data-testid='promotion-action-code-input']").Change("LAUNCH10");
        cut.Find("[data-testid='submit-promotion-code-action']").Click();
        cut.WaitForElement("[data-testid='rotate-promotion-code']").Click();
        cut.Find("[data-testid='promotion-action-code-input']").Change("ROTATE20");
        cut.Find("[data-testid='submit-promotion-code-action']").Click();
        cut.WaitForElement("[data-testid='promotion-issued-code']");

        await _promotions.Received(1).PublishAsync(
            eventId,
            draft.DefinitionId,
            Arg.Is<PromotionCodeRequest>(request => request.Code == "LAUNCH10"),
            Arg.Any<CancellationToken>());
        await _promotions.Received(1).RotateCodeAsync(
            eventId,
            draft.DefinitionId,
            Arg.Is<PromotionCodeRequest>(request => request.Code == "ROTATE20"),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("LAUNCH10");
        await Assert.That(cut.Markup).Contains("ROTATE20");
        cut.Find("[data-testid='dismiss-issued-code']").Click();
        await Assert.That(cut.Markup).DoesNotContain("ROTATE20");
    }

    [Test]
    public async Task FailedMutation_ShowsGenericErrorAndClearsSubmittedCode()
    {
        var eventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        var promotion = Promotion("Launch", "Published", "LAUNCH-••10", "rotate-promotion-code");
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [promotion]));
        _promotions.RotateCodeAsync(eventId, promotion.DefinitionId, Arg.Any<PromotionCodeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionCodeIssuedCommandResponseDto { Success = false, Message = "Rotation denied." });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='rotate-promotion-code']").Click();
        cut.Find("[data-testid='promotion-action-code-input']").Change("ROTATE20");
        cut.Find("[data-testid='submit-promotion-code-action']").Click();
        cut.WaitForElement("[data-testid='promotion-error']");

        await Assert.That(cut.Markup).Contains("Promotion could not be changed.");
        await Assert.That(cut.Markup).DoesNotContain("Rotation denied.");
        await Assert.That(cut.Markup).DoesNotContain("ROTATE20");
        await _announcer.Received().AnnounceAssertiveAsync("Promotion could not be changed.");
    }

    [Test]
    public async Task ParameterChange_ClearsIssuedCodeBeforeNextLoadCompletes()
    {
        var eventId = Guid.CreateVersion7();
        var nextEventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        _ticketing.GetCatalogAsync(nextEventId, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<EventTicketCatalogState?>().Task);
        var draft = Promotion("Launch", "Draft", null, "publish");
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [], "create-promotion"), Collection(eventId, [draft]));
        _promotions.CreateDraftAsync(eventId, Arg.Any<CreatePromotionDraftRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PromotionCodeIssuedCommandResponseDto { Success = true, IssuedCode = "PARAM30" });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='show-create-promotion']").Click();
        cut.Find("[data-testid='promotion-code-input']").Change("PARAM30");
        cut.Find("[data-testid='save-promotion-definition']").Click();
        cut.WaitForElement("[data-testid='promotion-issued-code']");
        _ = ChangeEventAsync(cut, nextEventId);

        cut.WaitForAssertion(() => Assert.That(cut.Markup).DoesNotContain("PARAM30"));
        await Assert.That(cut.FindAll("[data-testid='promotion-issued-code']")).IsEmpty();
    }

    [Test]
    public async Task StaleCreateCompletion_CannotExposeCodeAfterEventChange()
    {
        var eventId = Guid.CreateVersion7();
        var nextEventId = Guid.CreateVersion7();
        ConfigureCatalog(eventId);
        _ticketing.GetCatalogAsync(nextEventId, Arg.Any<CancellationToken>()).Returns(Catalog(nextEventId));
        _promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(eventId, [], "create-promotion"));
        _promotions.GetPromotionsAsync(nextEventId, CatalogVersionId, Arg.Any<CancellationToken>())
            .Returns(Collection(nextEventId, []));
        var completion = new TaskCompletionSource<PromotionCodeIssuedCommandResponseDto>();
        _promotions.CreateDraftAsync(eventId, Arg.Any<CreatePromotionDraftRequest>(), Arg.Any<CancellationToken>())
            .Returns(completion.Task);

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='show-create-promotion']").Click();
        cut.Find("[data-testid='promotion-code-input']").Change("STALE40");
        Task mutation = cut.InvokeAsync(() => cut.Find("[data-testid='save-promotion-definition']").Click());
        await ChangeEventAsync(cut, nextEventId);
        completion.SetResult(new PromotionCodeIssuedCommandResponseDto { Success = true, IssuedCode = "STALE40" });
        await mutation;

        await Assert.That(cut.Markup).DoesNotContain("STALE40");
        await Assert.That(cut.FindAll("[data-testid='promotion-issued-code']")).IsEmpty();
        await _announcer.DidNotReceive().AnnouncePoliteAsync("Promotion draft created. Copy the code now; it will not be shown again.");
    }

    [Test]
    public async Task Dispose_DuringPendingLoad_CancelsWithoutAccessibleError()
    {
        var eventId = Guid.CreateVersion7();
        CancellationToken captured = default;
        _ticketing.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            captured = (CancellationToken)call[1];
            return new TaskCompletionSource<EventTicketCatalogState?>().Task;
        });

        var cut = Render(eventId);
        cut.WaitForElement("[role='status']");
        cut.Instance.Dispose();

        await Assert.That(captured.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task RenderedEvidence_IsSanitizedAndDeterministic()
    {
        var eventId = Guid.Parse("019fcf29-f8d4-7d83-94c5-08ba1e7f1202");
        var published = Promotion("Published launch", "Published", "SAVE-••24", "revise-promotion", "revoke", "rotate-promotion-code");
        var markup = RenderEvidence(eventId, Collection(eventId, [published], "create-promotion"));
        var sanitized = Regex.Replace(markup, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "[guid]")
            .Replace("••", "--", StringComparison.Ordinal);
        sanitized = Regex.Replace(sanitized, @"mudinput[a-z0-9]+", "mudinput[id]");
        var evidencePath = Path.Combine(RepositoryRoot(), ".omo/evidence/phase17-ui/studio.html");
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath, $"<main data-evidence=\"studio-promotions\">{sanitized}</main>");

        await Assert.That(sanitized).Contains("Published launch");
        await Assert.That(sanitized).Contains("data-testid=\"revise-promotion\"");
        await Assert.That(sanitized).Contains("data-testid=\"rotate-promotion-code\"");
        await Assert.That(sanitized).Contains("Masked code: SAVE---24");
        await Assert.That(sanitized).DoesNotContain("aria-label=\"Masked promotion code\"");
        await Assert.That(sanitized).DoesNotContain(published.DefinitionId.ToString("D"));
    }

    private void ConfigureCatalog(Guid eventId) =>
        _ticketing.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(Catalog(eventId));

    private IRenderedComponent<StudioEventPromotions> Render(Guid eventId) =>
        _ctx.RenderMudComponent<StudioEventPromotions>(parameters => parameters
            .Add(component => component.EventId, eventId));

    private static Task ChangeEventAsync(IRenderedComponent<StudioEventPromotions> cut, Guid eventId) =>
        cut.InvokeAsync(() => ((IComponent)cut.Instance).SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(StudioEventPromotions.EventId)] = eventId
        })));

    private static string RenderEvidence(Guid eventId, PromotionManagementCollectionState state)
    {
        using var context = new BlazorTestContext();
        var ticketing = context.AddMockService<IEventTicketingService>();
        var promotions = context.AddMockService<IEventPromotionService>();
        ticketing.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(Catalog(eventId));
        promotions.GetPromotionsAsync(eventId, CatalogVersionId, Arg.Any<CancellationToken>()).Returns(state);
        var cut = context.RenderMudComponent<StudioEventPromotions>(parameters => parameters
            .Add(component => component.EventId, eventId));
        cut.WaitForElement("[data-testid='promotion-card']");
        return cut.Markup;
    }

    private static EventTicketCatalogState Catalog(Guid eventId) => new(
        eventId,
        CatalogVersionId,
        1,
        "USD",
        1,
        "DRAFT",
        "Draft",
        [],
        [],
        Links("self"));

    private static PromotionManagementCollectionState Collection(
        Guid eventId,
        IReadOnlyList<PromotionManagementItemState> items,
        params string[] relations) =>
        PromotionManagementCollectionState.Create(eventId, CatalogVersionId, items, Links(relations));

    private static PromotionManagementItemState Promotion(
        string label,
        string status,
        string? masked,
        params string[] relations) =>
        Promotion(label, status, masked, relations, true, []);

    private static PromotionManagementItemState Promotion(
        string label,
        string status,
        string? masked,
        IReadOnlyCollection<string> relations,
        bool includesAllTickets,
        IReadOnlyCollection<Guid> eligibleTicketTypeIds) => new(
        Guid.CreateVersion7(),
        label,
        status,
        "fixed",
        "USD",
        100,
        null,
        null,
        DateTimeOffset.Parse("2026-08-15T00:00:00Z"),
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
        10,
        1,
        includesAllTickets,
        eligibleTicketTypeIds,
        masked,
        Links(relations.ToArray()));

    private static Dictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(relation => relation, Link, StringComparer.Ordinal);

    private static HalLink Link(string relation) => new()
    {
        Href = $"/api/promotions/{WebUtility.UrlEncode(relation)}",
        Method = "POST",
        Title = relation
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
               && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
