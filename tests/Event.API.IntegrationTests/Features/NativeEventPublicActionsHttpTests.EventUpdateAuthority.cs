using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task DirectEventUpdateRejectsSameTenantNonOwner()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var before = await db.Events.AsNoTracking().SingleAsync(row => row.Id == data.DraftId);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventCommandHandler>(scope.ServiceProvider);

        await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(new UpdateEventCommand
        {
            EventId = before.Id,
            ExpectedConcurrencyStamp = before.ConcurrencyStamp,
            UpdateEventDto = new() { Title = new() { Value = "Unauthorized title" } }
        }, default));

        await Assert.That((await db.Events.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Title)
            .IsEqualTo(before.Title);
    }

    [Test]
    public async Task EventUpdateRechecksRevokedGrantAfterProviderBeforeNativeWrite()
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var before = await db.Events.AsNoTracking().SingleAsync(row => row.Id == data.DraftId);
        var grant = EventRoleAssignment.Create(db.TenantContext!.TenantId, before.Id,
            data.OutsiderId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
            DateTime.UnixEpoch, null, data.OwnerId);
        db.EventRoleAssignments.Add(grant);
        await db.SaveChangesAsync();
        var command = new UpdateEventCommand
        {
            EventId = before.Id,
            ExpectedConcurrencyStamp = before.ConcurrencyStamp,
            UpdateEventDto = new() { Title = new() { Value = "Must not survive revocation" } }
        };
        await scope.ServiceProvider.GetRequiredService<RequestAuthorization<UpdateEventCommand>>()
            .AuthorizeAsync(command, scope.ServiceProvider.GetRequiredService<ILogger<UpdateEventCommandHandler>>(), default);
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.TenantContext = new AuthorityTenant(db.TenantContext.TenantId);
            var current = await revocation.EventRoleAssignments.SingleAsync(row => row.Id == grant.Id, token);
            current.Revoke(data.OwnerId, DateTime.UtcNow);
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var handler = ActivatorUtilities.CreateInstance<UpdateEventCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(command, deadline.Token);

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That((await db.Events.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Title)
            .IsEqualTo(before.Title);
    }

    [Test]
    public async Task DirectEventUpdatePreservesCurrentOwnersPositivePath()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var before = await db.Events.AsNoTracking().SingleAsync(row => row.Id == data.DraftId);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new UpdateEventCommand
        {
            EventId = before.Id,
            ExpectedConcurrencyStamp = before.ConcurrencyStamp,
            UpdateEventDto = new UpdateEventDto { Title = new() { Value = "Current authorized title" } }
        }, default);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await db.Events.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Title)
            .IsEqualTo("Current authorized title");
    }

    [Test]
    public async Task EventUpdateRejectsAPretrackedPublisherSubstitution()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tracked = await db.Events.SingleAsync(row => row.Id == data.DraftId);
        var persistedActor = tracked.ActorId;
        tracked.ActorId = Guid.CreateVersion7();
        var handler = ActivatorUtilities.CreateInstance<UpdateEventCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new UpdateEventCommand
        {
            EventId = tracked.Id,
            ExpectedConcurrencyStamp = tracked.ConcurrencyStamp,
            UpdateEventDto = new() { Title = new() { Value = "Must not mutate a supplied graph" } }
        }, default);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That((await db.Events.AsNoTracking().SingleAsync(row => row.Id == tracked.Id)).ActorId)
            .IsEqualTo(persistedActor);
    }
}
