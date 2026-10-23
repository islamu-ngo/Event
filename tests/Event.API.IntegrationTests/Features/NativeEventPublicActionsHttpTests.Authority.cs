using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventPublicActions.Handlers.Commands;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task DirectHandlerRejectsSameTenantNonOwnerWithoutDependingOnDecorator()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var before = (await State(factory, data.ActionId))!;
        using var scope = Scope(factory, data.OutsiderId);
        var handler = ActivatorUtilities.CreateInstance<DeleteEventPublicActionCommandHandler>(scope.ServiceProvider);

        await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(new DeleteEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.ActionId,
            ExpectedConcurrencyStamp = before.Stamp
        }, default));

        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(before);
    }

    [Test]
    public async Task DirectHandlerRejectsForeignTrackedActionWithForgedParent()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var before = (await State(factory, data.ForeignActionId, data.ForeignTenantId))!;
        using var scope = Scope(factory, data.OwnerId);
        await using (var foreign = await scope.ServiceProvider
                         .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync())
        {
            foreign.TenantContext = new AuthorityTenant(data.ForeignTenantId);
            var tracked = await foreign.EventPublicActions.AsNoTracking()
                .SingleAsync(row => row.Id == data.ForeignActionId);
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Attach(tracked);
        }
        var handler = ActivatorUtilities.CreateInstance<DeleteEventPublicActionCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new DeleteEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.ForeignActionId,
            ExpectedConcurrencyStamp = before.Stamp
        }, default);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await State(factory, data.ForeignActionId, data.ForeignTenantId)).IsEqualTo(before);
    }

    [Test]
    public async Task RevocationBetweenProviderAndTransactionRejectsTrackedGrant()
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        var before = (await State(factory, data.ActionId))!;
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var grant = EventRoleAssignment.Create(db.TenantContext!.TenantId, data.PublicId,
            data.OutsiderId, (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
            DateTime.UnixEpoch, null, data.OwnerId);
        db.EventRoleAssignments.Add(grant);
        await db.SaveChangesAsync();
        var command = new DeleteEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.ActionId,
            ExpectedConcurrencyStamp = before.Stamp
        };
        await scope.ServiceProvider.GetRequiredService<RequestAuthorization<DeleteEventPublicActionCommand>>()
            .AuthorizeAsync(command, scope.ServiceProvider.GetRequiredService<ILogger<DeleteEventPublicActionCommandHandler>>(), default);

        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.TenantContext = new AuthorityTenant(db.TenantContext.TenantId);
            var current = await revocation.EventRoleAssignments.SingleAsync(row => row.Id == grant.Id, token);
            current.Revoke(data.OwnerId, DateTime.UtcNow);
            await revocation.SaveChangesAsync(token);
        });

        var handler = ActivatorUtilities.CreateInstance<DeleteEventPublicActionCommandHandler>(scope.ServiceProvider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await handler.ExecuteAsync(command, deadline.Token);

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(before);
    }

    [Test]
    public async Task DirectHandlerAcceptsCurrentPersistedOwnerAndDeletesOnlyExactAction()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var before = (await State(factory, data.ActionId))!;
        var other = await State(factory, data.EarlierId);
        using var scope = Scope(factory, data.OwnerId);
        var handler = ActivatorUtilities.CreateInstance<DeleteEventPublicActionCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new DeleteEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.ActionId,
            ExpectedConcurrencyStamp = before.Stamp
        }, default);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await State(factory, data.ActionId)).IsNull();
        await Assert.That(await State(factory, data.EarlierId)).IsEqualTo(other);
    }

    private sealed record AuthorityTenant(Guid TenantId) : ITenantContext;

    [Test]
    public async Task TrackedActionCannotClaimAnotherSameTenantParent()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var before = (await State(factory, data.OtherActionId))!;
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var forged = await db.EventPublicActions.SingleAsync(row => row.Id == data.OtherActionId);
        forged.EventId = data.PublicId;
        var handler = ActivatorUtilities.CreateInstance<DeleteEventPublicActionCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new DeleteEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.OtherActionId,
            ExpectedConcurrencyStamp = before.Stamp
        }, default);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await State(factory, data.OtherActionId)).IsEqualTo(before);
    }

    [Test]
    public async Task NativeAuthorityCannotEscapeItsOwningTransaction()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventPublicActionRepository>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetAuthorizedEventForMutationAsync(
            tenant.TenantId, data.OwnerId, data.PublicId, default));
    }

    [Test]
    public async Task PlatformAdministrationCannotReplaceExactPublicActionAuthority()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = data.OutsiderId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IEventPublicActionRepository>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var current = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteSerializableAsync(token => repository.GetAuthorizedEventForMutationAsync(
                tenant.TenantId, data.OutsiderId, data.PublicId, token));

        await Assert.That(current).IsNull();
    }

    [Test]
    public async Task TargetResolutionUsesPersistedCurrentTenantAndIgnoresClaimedOwner()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var forged = new EventAuthorizationFacts(data.ForeignTenantId, data.PublicId,
            Guid.CreateVersion7(), data.OutsiderId, null, null, null, null, null, null, null, null);

        await Assert.That(await repository.GetAuthorizationTargetByIdAsync(data.ForeignId, default)).IsNull();
        var resolved = await resolver.ResolveAsync(new object(), ResourceKinds.Event,
            AuthorizationActions.Update, data.PublicId.ToString("D"), forged, default);
        var persisted = (EventAuthorizationFacts)resolved.Facts!;
        await Assert.That(persisted.TenantId).IsEqualTo(tenant.TenantId);
        await Assert.That(persisted.UserId).IsEqualTo((Guid?)data.OwnerId);
        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(new object(),
            ResourceKinds.Event, AuthorizationActions.Update, data.ForeignId.ToString("D"), forged, default));
    }

    private sealed class BeforeAuthorityTransaction : DbTransactionInterceptor
    {
        private DbContext? _target;
        private Func<CancellationToken, Task>? _operation;
        public bool Applied { get; private set; }

        public void Arm(DbContext target, Func<CancellationToken, Task> operation)
        {
            _target = target;
            _operation = operation;
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)
                && Interlocked.Exchange(ref _operation, null) is { } operation)
            {
                await operation(cancellationToken);
                Applied = true;
            }
            return result;
        }
    }
}
