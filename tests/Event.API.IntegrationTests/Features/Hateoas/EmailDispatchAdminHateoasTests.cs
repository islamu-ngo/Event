using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

[Category(TestCategories.Email)]
public sealed class EmailDispatchAdminHateoasTests
{
    [Test]
    public async Task ProcessorControlExposesOnlyCurrentPauseAndRateAffordances()
    {
        var policy = new EmailDispatchProcessorControlDetailLinkPolicy();
        var activeLinks = policy.GetLinks(new EmailDispatchProcessorControlDto(), user: null).ToList();
        var pausedLinks = policy.GetLinks(new EmailDispatchProcessorControlDto
        {
            IsPaused = true,
            GlobalSmtpRateLimitPerMinuteOverride = 60
        }, user: null).ToList();

        await Assert.That(activeLinks.Any(link => link.Rel == "pause")).IsTrue();
        await Assert.That(activeLinks.Any(link => link.Rel == "resume")).IsFalse();
        await Assert.That(activeLinks.Any(link => link.Rel == "clear-rate-limit")).IsFalse();
        await Assert.That(pausedLinks.Any(link => link.Rel == "pause")).IsFalse();
        await Assert.That(pausedLinks.Any(link => link.Rel == "resume")).IsTrue();
        await Assert.That(pausedLinks.Any(link => link.Rel == "clear-rate-limit")).IsTrue();
        await Assert.That(pausedLinks.Single(link => link.Rel == "resume").PermissionResourceKind)
            .IsEqualTo(ResourceKinds.InstanceSetting);
    }

    [Test]
    public async Task DeferredStatusRows_ExposeReplayAndParkLinks()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var dto = CreateStatus(tenantId, outboxId, "DeadLettered");
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(dto, user: null).ToList();

        var replay = links.Single(link => link.Rel == "replay");
        await Assert.That(replay.RouteName).IsEqualTo(RouteNames.ReplayEmailDispatch);
        await Assert.That(replay.Method).IsEqualTo("POST");
        await Assert.That(replay.RequiresAuth).IsTrue();
        await Assert.That(replay.PermissionResourceKind).IsEqualTo(ResourceKinds.EmailDispatch);
        await Assert.That(replay.PermissionAction).IsEqualTo(AuthorizationActions.EmailDispatches.Replay);
        await Assert.That(replay.PermissionResourceId).IsEqualTo(outboxId.ToString());
        await Assert.That(replay.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
        await AssertPermissionFacts(replay, tenantId, outboxId);
        await Assert.That(GetRouteValue<Guid>(replay.RouteValues, "tenantId")).IsEqualTo(tenantId);
        await Assert.That(GetRouteValue<Guid>(replay.RouteValues, "outboxId")).IsEqualTo(outboxId);

        var park = links.Single(link => link.Rel == "park");
        await Assert.That(park.RouteName).IsEqualTo(RouteNames.ParkEmailDispatch);
        await Assert.That(park.Method).IsEqualTo("PUT");
        await Assert.That(park.RequiresAuth).IsTrue();
        await Assert.That(park.PermissionResourceKind).IsEqualTo(ResourceKinds.EmailDispatch);
        await Assert.That(park.PermissionAction).IsEqualTo(AuthorizationActions.EmailDispatches.Park);
        await Assert.That(park.PermissionResourceId).IsEqualTo(outboxId.ToString());
        await Assert.That(park.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
        await AssertPermissionFacts(park, tenantId, outboxId);
        await Assert.That(GetRouteValue<Guid>(park.RouteValues, "tenantId")).IsEqualTo(tenantId);
        await Assert.That(GetRouteValue<Guid>(park.RouteValues, "outboxId")).IsEqualTo(outboxId);

        var resolve = links.Single(link => link.Rel == "resolve-without-replay");
        await Assert.That(resolve.RouteName).IsEqualTo(RouteNames.ResolveEmailDispatchWithoutReplay);
        await Assert.That(resolve.Method).IsEqualTo("POST");
        await Assert.That(resolve.PermissionAction).IsEqualTo(AuthorizationActions.EmailDispatches.Resolve);
        await AssertPermissionFacts(resolve, tenantId, outboxId);
    }

    [Test]
    public async Task SentStatusRows_DoNotExposeReplayOrParkLinks()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(Guid.NewGuid(), Guid.NewGuid(), "Sent"), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsFalse();
    }

    [Test]
    public async Task ParkedStatusRows_ExposeReplayButNotParkLink()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(Guid.NewGuid(), Guid.NewGuid(), "Parked"), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsTrue();
    }

    [Test]
    public async Task ProcessingStatusRows_ExposeNoMutationLinks()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(Guid.NewGuid(), Guid.NewGuid(), "Processing"), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsFalse();
    }

    [Test]
    public async Task UnknownStatusRowsExposeReconcileAndResolveButNotReplay()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(Guid.NewGuid(), Guid.NewGuid(), "Unknown"), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "reconcile")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
    }

    [Test]
    public async Task RedactedStatusRowsExposeNoMutationLinks()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();
        var dto = CreateStatus(Guid.NewGuid(), Guid.NewGuid(), "DeadLettered");
        dto = dto with { ContentRedactedAt = DateTime.UtcNow };

        var links = policy.GetItemLinks(dto, user: null).ToList();

        await Assert.That(links).IsEmpty();
    }

    private static EmailDispatchStatusDto CreateStatus(Guid tenantId, Guid outboxId, string deliveryStatus) => new()
    {
        TenantId = tenantId,
        OutboxId = outboxId,
        SourceType = "event_registration",
        SourceId = Guid.NewGuid(),
        DeliveryStatus = deliveryStatus,
        AttemptCount = 1,
        CorrelationId = Guid.NewGuid().ToString("N")
    };

    private static T? GetRouteValue<T>(object? routeValues, string name)
    {
        if (routeValues is null)
            return default;

        var property = routeValues.GetType().GetProperty(name);
        var value = property?.GetValue(routeValues);
        return value is T typedValue ? typedValue : default;
    }

    /// <summary>
    /// Email-dispatch administration is decided by tenant authority alone. The outbox row, its source and
    /// its delivery status select which link is advertised; none of them is a policy input.
    /// </summary>
    private static async Task AssertPermissionFacts(LinkDefinition link, Guid tenantId, Guid outboxId)
    {
        await Assert.That(link.PermissionResourceId).IsEqualTo(outboxId.ToString());
        await Assert.That(link.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));
    }
}
