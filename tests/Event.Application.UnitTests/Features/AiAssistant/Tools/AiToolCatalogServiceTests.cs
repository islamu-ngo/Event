using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class AiToolCatalogServiceTests
{
    [Test]
    public async Task GetCatalogWhenRouteWorkflowContextAndHalMatchReturnsProposalAvailableItem()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery(halRels: HalSet("create-event"))).Single();

        await Assert.That(item.Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(item.CanRequestProposal).IsTrue();
        await Assert.That(item.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(item.AvailabilityCode).IsEqualTo(AiToolCatalogAvailabilityCodes.Available);
    }

    [Test]
    public async Task GetCatalogWhenHalAffordanceDiffersByCaseStillReturnsAvailableItem()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery(halRels: HalSet("CREATE-EVENT"))).Single();

        await Assert.That(item.CanRequestProposal).IsTrue();
        await Assert.That(item.AvailabilityCode).IsEqualTo(AiToolCatalogAvailabilityCodes.Available);
    }

    [Test]
    public async Task GetCatalogWhenHalAffordanceIsMissingKeepsItemNonExecutable()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery()).Single();

        await Assert.That(item.CanRequestProposal).IsFalse();
        await Assert.That(item.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(item.AvailabilityCode).IsEqualTo(AiToolCatalogAvailabilityCodes.MissingHalAffordance);
        await Assert.That(item.AvailabilityReason).Contains("API/HAL");
    }

    [Test]
    public async Task GetCatalogWhenEventManagementRouteHasEditHalReturnsUpdateDraftItem()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery(
            routePath: "/events/{eventId}",
            workflowScope: "event-management",
            contextScope: "event-management-context",
            halRels: HalSet("edit"))).Single(candidate => candidate.Kind == AiProposedActionKind.UpdateEventDraft);

        await Assert.That(item.Kind).IsEqualTo(AiProposedActionKind.UpdateEventDraft);
        await Assert.That(item.CanRequestProposal).IsTrue();
        await Assert.That(item.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(item.Metadata.RequiredHalLinkRel).IsEqualTo("edit");
    }

    [Test]
    public async Task GetCatalogWhenEventManagementRouteHasPublishHalReturnsPublishItem()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery(
            routePath: "/events/{eventId}",
            workflowScope: "event-management",
            contextScope: "event-management-context",
            halRels: HalSet("publish"))).Single(candidate => candidate.Kind == AiProposedActionKind.PublishEvent);

        await Assert.That(item.Kind).IsEqualTo(AiProposedActionKind.PublishEvent);
        await Assert.That(item.CanRequestProposal).IsTrue();
        await Assert.That(item.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(item.Metadata.RequiredHalLinkRel).IsEqualTo("publish");
    }

    [Test]
    public async Task GetCatalogWhenEventManagementRouteHasDeleteHalReturnsDeleteItem()
    {
        var item = new AiToolCatalogService().GetCatalog(CreateQuery(
            routePath: "/events/{eventId}",
            workflowScope: "event-management",
            contextScope: "event-management-context",
            halRels: HalSet("delete"))).Single(candidate => candidate.Kind == AiProposedActionKind.DeleteEvent);

        await Assert.That(item.Kind).IsEqualTo(AiProposedActionKind.DeleteEvent);
        await Assert.That(item.CanRequestProposal).IsTrue();
        await Assert.That(item.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(item.Metadata.RequiredHalLinkRel).IsEqualTo("delete");
        await Assert.That(item.Metadata.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task GetCatalogWhenEventManagementRouteHasModerationHalReturnsModerationItems()
    {
        var catalog = new AiToolCatalogService().GetCatalog(CreateQuery(
            routePath: "/events/{eventId}",
            workflowScope: "event-management",
            contextScope: "event-management-context",
            halRels: HalSet("moderate-light", "moderate-heavy", "unmoderate")));

        await Assert.That(catalog.Single(item => item.Kind == AiProposedActionKind.LightModerateEvent).CanRequestProposal).IsTrue();
        await Assert.That(catalog.Single(item => item.Kind == AiProposedActionKind.HeavyModerateEvent).Metadata.DestructiveHint).IsTrue();
        await Assert.That(catalog.Single(item => item.Kind == AiProposedActionKind.UnmoderateEvent).Metadata.RequiredHalLinkRel).IsEqualTo("unmoderate");
    }

    [Test]
    public async Task GetCatalogWhenEventManagementRouteHasEditHalReturnsAspectItems()
    {
        var catalog = new AiToolCatalogService().GetCatalog(CreateQuery(
            routePath: "/events/{eventId}",
            workflowScope: "event-aspects",
            contextScope: "event-aspect-context",
            halRels: HalSet("edit")));

        await Assert.That(catalog.Select(item => item.Kind)).Contains(AiProposedActionKind.UpsertEventIslamicAspect);
        await Assert.That(catalog.Select(item => item.Kind)).Contains(AiProposedActionKind.DeleteEventIslamicAspect);
        await Assert.That(catalog.Select(item => item.Kind)).Contains(AiProposedActionKind.UpsertEventTechAspect);
        await Assert.That(catalog.Select(item => item.Kind)).Contains(AiProposedActionKind.DeleteEventTechAspect);
        await Assert.That(catalog.Single(item => item.Kind == AiProposedActionKind.DeleteEventTechAspect).Metadata.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task GetCatalogWhenRouteDoesNotMatchReturnsNoItems()
    {
        var catalog = new AiToolCatalogService().GetCatalog(CreateQuery(routePath: "/admin/settings", halRels: HalSet("create-event")));

        await Assert.That(catalog).IsEmpty();
    }

    [Test]
    public async Task GetCatalogWhenTenantIsMissingReturnsNoItems()
    {
        var catalog = new AiToolCatalogService().GetCatalog(CreateQuery(tenantId: Guid.Empty, halRels: HalSet("create-event")));

        await Assert.That(catalog).IsEmpty();
    }

    [Test]
    public async Task GetCatalogWhenPrincipalIsAnonymousReturnsNoItems()
    {
        var catalog = new AiToolCatalogService().GetCatalog(CreateQuery(isAuthenticated: false, halRels: HalSet("create-event")));

        await Assert.That(catalog).IsEmpty();
    }

    private static HashSet<string> HalSet(params string[] rels) => new(rels, StringComparer.OrdinalIgnoreCase);

    private static AiToolCatalogQuery CreateQuery(
        Guid? tenantId = null,
        bool isAuthenticated = true,
        string routePath = "/events",
        string workflowScope = "event-drafting",
        string contextScope = "selected-references",
        IReadOnlySet<string>? halRels = null)
        => new(
            tenantId ?? Guid.CreateVersion7(),
            isAuthenticated,
            AiToolCatalogPrincipalKind.User,
            routePath,
            workflowScope,
            contextScope,
            halRels ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
