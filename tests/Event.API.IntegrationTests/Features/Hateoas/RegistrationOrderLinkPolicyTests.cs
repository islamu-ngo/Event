using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class RegistrationOrderLinkPolicyTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task GetLinks_UsesOrderScopedViewAndCancelCandidates()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.Draft
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo(
            [LinkRelations.Self, LinkRelations.ViewParticipants, LinkRelations.Cancel]);
        await Assert.That(links.All(link => link.PermissionResourceKind == ResourceKinds.RegistrationOrder)).IsTrue();
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).PermissionAction)
            .IsEqualTo(AuthorizationActions.RegistrationOrders.View);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Cancel).PermissionAction)
            .IsEqualTo(AuthorizationActions.RegistrationOrders.Cancel);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.ViewParticipants).PermissionAction)
            .IsEqualTo(AuthorizationActions.RegistrationOrders.View);
    }

    [Test]
    public async Task GetLinks_WhenReadyForCheckoutWithoutPromotion_EmitsApplyButNotRemove()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.ReadyForCheckout,
            StatusCode = "READY_FOR_CHECKOUT"
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo(
            [LinkRelations.Self, LinkRelations.ViewParticipants, LinkRelations.Continue, LinkRelations.ApplyPromotion, LinkRelations.Finalize, LinkRelations.Cancel]);
        await Assert.That(links.Select(link => link.Rel)).DoesNotContain(LinkRelations.RemovePromotion);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Continue).RouteName)
            .IsEqualTo(RouteNames.ContinueAuthenticatedRegistrationOrder);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.ApplyPromotion).RouteName)
            .IsEqualTo(RouteNames.ApplyAuthenticatedRegistrationOrderPromotion);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Finalize).RouteName)
            .IsEqualTo(RouteNames.FinalizeAuthenticatedRegistrationOrder);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Continue).PermissionAction)
            .IsEqualTo("continue");
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Finalize).PermissionAction)
            .IsEqualTo("finalize");
    }

    [Test]
    public async Task GetLinks_WhenReadyForCheckoutWithPromotion_EmitsRemoveButNotApply()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.ReadyForCheckout,
            StatusCode = "READY_FOR_CHECKOUT",
            AppliedPromotionDisplayLabel = "Launch discount"
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.RemovePromotion);
        await Assert.That(links.Select(link => link.Rel)).DoesNotContain(LinkRelations.ApplyPromotion);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.RemovePromotion).RouteName)
            .IsEqualTo(RouteNames.RemoveAuthenticatedRegistrationOrderPromotion);
    }

    [Test]
    public async Task GetLinks_WhenAwaitingRequirements_EmitsContinueButNotFinalize()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.AwaitingRequirements,
            StatusCode = "AWAITING_REQUIREMENTS"
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo(
            [LinkRelations.Self, LinkRelations.ViewParticipants, LinkRelations.Continue, LinkRelations.RequirementProgress, LinkRelations.Cancel]);
        LinkDefinition progress = links.Single(link => link.Rel == LinkRelations.RequirementProgress);
        await Assert.That(progress.RouteName)
            .IsEqualTo(RouteNames.GetAuthenticatedNativeRegistrationRequirementProgress);
        await Assert.That(progress.Method).IsEqualTo("GET");
    }

    [Test]
    public async Task GetLinks_WhenAwaitingPaidCheckout_UsesPurchaserAndExactCommercePermissions()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.AwaitingPayment,
            StatusCode = "AWAITING_PAYMENT",
            TotalDueMinor = 1_000,
            ExpiresAt = UtcNow.AddMinutes(30),
            PaidCheckoutActivationAvailable = true
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.StartPayment);
        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.PaymentStatus);
        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.StudioPaymentStatus);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.StartPayment).PermissionAction)
            .IsEqualTo(AuthorizationActions.RegistrationOrders.Continue);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.StudioPaymentStatus).PermissionAction)
            .IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.StudioPaymentStatus).PermissionResourceKind)
            .IsEqualTo(ResourceKinds.Event);
    }

    [Test]
    public async Task GetLinks_WhenStopSaleIsActiveOmitsNewSaleButPreservesPaymentStatusRead()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.AwaitingPayment,
            StatusCode = "AWAITING_PAYMENT",
            TotalDueMinor = 1_000,
            ExpiresAt = UtcNow.AddMinutes(5)
        };
        var policy = new RegistrationOrderLinkPolicy(new FixedTimeProvider(UtcNow));

        var links = policy.GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).DoesNotContain(LinkRelations.StartPayment);
        await Assert.That(links.Select(link => link.Rel)).DoesNotContain(LinkRelations.PaymentAcceptance);
        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.PaymentStatus);
    }

    [Test]
    public async Task GetLinks_WhenAwaitingPaymentIsExpired_OmitsStartPayment()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)Explore.Domain.Enums.RegistrationOrderStatusEnum.AwaitingPayment,
            StatusCode = "AWAITING_PAYMENT",
            TotalDueMinor = 1_000,
            ExpiresAt = UtcNow.AddSeconds(-1)
        };

        var links = CreatePolicy().GetLinks(order, null).ToList();

        await Assert.That(links.Select(link => link.Rel)).DoesNotContain(LinkRelations.StartPayment);
        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.PaymentStatus);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private static RegistrationOrderLinkPolicy CreatePolicy() => new(new FixedTimeProvider(UtcNow));
}
