using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Hateoas;
using Explore.Domain;
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
        var dto = CreateStatus(tenantId: tenantId, outboxId: outboxId, deliveryStatus: EmailDispatchStatus.DeadLettered);
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

        var links = policy.GetItemLinks(CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.Sent), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsFalse();
    }

    [Test]
    public async Task ParkedStatusRows_ExposeReplayButNotParkLink()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.Parked), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsTrue();
    }

    [Test]
    public async Task ProcessingStatusRows_ExposeNoMutationLinks()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.Processing), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsFalse();
    }

    [Test]
    public async Task UnknownStatusRowsExposeReconcileAndResolveButNotReplay()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.Unknown), user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == "reconcile")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "resolve-without-replay")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "replay")).IsFalse();
    }

    [Test]
    public async Task RedactedStatusRowsExposeNoMutationLinks()
    {
        var policy = new EmailDispatchStatusCollectionLinkPolicy();
        var dto = CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.DeadLettered);
        dto = dto with { ContentRedactedAt = DateTime.UtcNow };

        var links = policy.GetItemLinks(dto, user: null).ToList();

        await Assert.That(links).IsEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CapabilityParkedRowsExposePermissionQualifiedOperatorParkCandidate(bool detail)
    {
        var dto = CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: EmailDispatchStatus.Parked)
            with { ParkReason = EmailDispatchParkReason.CapabilityUnavailable };

        var links = detail
            ? new EmailDispatchStatusDetailLinkPolicy().GetLinks(dto, user: null)
            : new EmailDispatchStatusCollectionLinkPolicy().GetItemLinks(dto, user: null);
        var park = links.Single(link => link.Rel == "park");

        await Assert.That(park.RouteName).IsEqualTo(RouteNames.ParkEmailDispatch);
        await Assert.That(park.Method).IsEqualTo("PUT");
        await Assert.That(park.RequiresAuth).IsTrue();
        await Assert.That(park.PermissionResourceKind).IsEqualTo(ResourceKinds.EmailDispatch);
        await Assert.That(park.PermissionAction).IsEqualTo(AuthorizationActions.EmailDispatches.Park);
        await Assert.That(park.PermissionScope?.TenantId).IsEqualTo(dto.TenantId.ToString());
        await AssertPermissionFacts(link: park, tenantId: dto.TenantId, outboxId: dto.OutboxId);
        await Assert.That(GetRouteValue<Guid>(park.RouteValues, "tenantId")).IsEqualTo(dto.TenantId);
        await Assert.That(GetRouteValue<Guid>(park.RouteValues, "outboxId")).IsEqualTo(dto.OutboxId);
    }

    [Test]
    [Arguments(EmailDispatchStatus.Parked, EmailDispatchParkReason.Operator)]
    [Arguments(EmailDispatchStatus.Parked, null)]
    [Arguments(EmailDispatchStatus.Parked, (EmailDispatchParkReason)999)]
    [Arguments(EmailDispatchStatus.Unknown, EmailDispatchParkReason.CapabilityUnavailable)]
    [Arguments(EmailDispatchStatus.Processing, EmailDispatchParkReason.CapabilityUnavailable)]
    [Arguments(EmailDispatchStatus.Sent, EmailDispatchParkReason.CapabilityUnavailable)]
    [Arguments(EmailDispatchStatus.Skipped, EmailDispatchParkReason.CapabilityUnavailable)]
    [Arguments((EmailDispatchStatus)999, EmailDispatchParkReason.CapabilityUnavailable)]
    public async Task IneligibleRowsOmitParkCandidateFromBothPolicies(
        EmailDispatchStatus deliveryStatus,
        EmailDispatchParkReason? parkReason)
    {
        var dto = CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: deliveryStatus)
            with { ParkReason = parkReason };

        var detailLinks = new EmailDispatchStatusDetailLinkPolicy().GetLinks(dto, user: null).ToList();
        var collectionLinks = new EmailDispatchStatusCollectionLinkPolicy().GetItemLinks(dto, user: null).ToList();

        await Assert.That(detailLinks.Any(link => link.Rel == "park")).IsFalse();
        await Assert.That(collectionLinks.Any(link => link.Rel == "park")).IsFalse();
    }

    [Test]
    [Arguments(EmailDispatchStatus.Pending)]
    [Arguments(EmailDispatchStatus.RetryScheduled)]
    [Arguments(EmailDispatchStatus.DeadLettered)]
    public async Task EligibleRowsExposeParkCandidateFromBothPolicies(EmailDispatchStatus deliveryStatus)
    {
        var dto = CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(), deliveryStatus: deliveryStatus);

        await Assert.That(new EmailDispatchStatusDetailLinkPolicy().GetLinks(dto, user: null)
            .Any(link => link.Rel == "park")).IsTrue();
        await Assert.That(new EmailDispatchStatusCollectionLinkPolicy().GetItemLinks(dto, user: null)
            .Any(link => link.Rel == "park")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RedactedOrUndefinedRowsExposeNoMutationCandidatesFromBothPolicies(bool redacted)
    {
        var dto = CreateStatus(tenantId: Guid.NewGuid(), outboxId: Guid.NewGuid(),
            deliveryStatus: redacted ? EmailDispatchStatus.Parked : (EmailDispatchStatus)999) with
        {
            ParkReason = EmailDispatchParkReason.CapabilityUnavailable,
            ContentRedactedAt = redacted ? DateTime.UtcNow : null
        };

        await Assert.That(new EmailDispatchStatusDetailLinkPolicy().GetLinks(dto, user: null).ToList()).IsEmpty();
        await Assert.That(new EmailDispatchStatusCollectionLinkPolicy().GetItemLinks(dto, user: null).ToList()).IsEmpty();
    }

    private static EmailDispatchStatusDto CreateStatus(Guid tenantId, Guid outboxId, EmailDispatchStatus deliveryStatus) => new()
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
