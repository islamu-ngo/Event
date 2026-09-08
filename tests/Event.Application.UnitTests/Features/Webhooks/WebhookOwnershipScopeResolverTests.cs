using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Webhooks;

public sealed class WebhookOwnershipScopeResolverTests
{
    [Test]
    public async Task ResolveAsync_UserOwner_UsesResolvedLocalUserIdentity()
    {
        var tenantId = Guid.NewGuid();
        var localUserId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        var currentUserService = Substitute.For<ICurrentUserService>();
        var tenantUserRepository = Substitute.For<ITenantUserRepository>();
        tenantContext.TenantId.Returns(tenantId);
        currentUserService.UserId.Returns(localUserId);
        tenantUserRepository
            .GetByTenantAndUserAsync(tenantId, localUserId, Arg.Any<CancellationToken>())
            .Returns(CreateActiveTenantUser(tenantId, localUserId));
        var resolver = CreateResolver(tenantContext, currentUserService, tenantUserRepository);

        var resolution = await resolver.ResolveAsync(
            (int)WebhookConsumerKind.User,
            requestedOwnerId: null,
            CancellationToken.None);

        await Assert.That(resolution.IsResolved).IsTrue();
        await Assert.That(resolution.Scope!.Kind).IsEqualTo(WebhookConsumerKind.User);
        await Assert.That(resolution.Scope.TenantId).IsEqualTo(tenantId);
        await Assert.That(resolution.Scope.UserId).IsEqualTo(localUserId);
    }

    [Test]
    public async Task ResolveAsync_UserOwner_WithoutResolvedLocalIdentity_FailsClosed()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        var currentUserService = Substitute.For<ICurrentUserService>();
        var tenantUserRepository = Substitute.For<ITenantUserRepository>();
        currentUserService.UserId.Returns((Guid?)null);
        var resolver = CreateResolver(tenantContext, currentUserService, tenantUserRepository);

        var resolution = await resolver.ResolveAsync(
            (int)WebhookConsumerKind.User,
            requestedOwnerId: null,
            CancellationToken.None);

        await Assert.That(resolution.IsResolved).IsFalse();
        await Assert.That(resolution.FailureCode).IsEqualTo("webhook_user_identity_unavailable");
        await tenantUserRepository.DidNotReceiveWithAnyArgs()
            .GetByTenantAndUserAsync(default, default, default);
    }

    private static WebhookOwnershipScopeResolver CreateResolver(
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        ITenantUserRepository tenantUserRepository) =>
        new(
            tenantContext,
            currentUserService,
            Substitute.For<IInstanceBootstrapStateRepository>(),
            Substitute.For<IOrganizationRepository>(),
            Substitute.For<IGroupRepository>(),
            tenantUserRepository,
            Substitute.For<IWebhookConsumerRepository>(),
            Substitute.For<IWebhookEndpointRepository>(),
            Substitute.For<IWebhookMessageRepository>(),
            Substitute.For<IWebhookDeliveryAttemptRepository>());

    private static TenantUser CreateActiveTenantUser(Guid tenantId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Tenant = null!,
        UserId = userId,
        User = null!,
        StatusId = (int)TenantUserStatusEnum.Active
    };
}
