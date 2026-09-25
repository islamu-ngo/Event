using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceAuthoritySnapshotPersistenceTests
{
    [Test]
    public async Task DisabledDeliveryKeepsExistingResourceRepairButStopsNewResourceCreation()
    {
        var scope = await database.SeedScopeAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        Guid userId;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = userId, User = null!,
                ActorId = scope.ActorId, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
            seed.EventRoleAssignments.Add(EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Now.AddMinutes(-1), null, userId));
            seed.Add(resource);
            await seed.SaveChangesAsync();
        }
        var defaults = EventResourceGovernancePolicy.Default(long.MaxValue);
        var disabled = EventResourceGovernancePolicy.Create([], defaults.EnabledAudiences,
            defaults.PermittedFileTypes, defaults.MaxUploadBytes, false, [], defaults.AuditRetentionDays,
            defaults.MaxActiveResources, long.MaxValue);
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(scope.TenantAId, Arg.Any<CancellationToken>()).Returns(disabled);
        await using var context = database.CreateContext();
        var reader = new EventResourceAuthoritySnapshotReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context), governance);
        EventResourceAuthorityRequest update = new(scope.TenantAId, resource.Id, userId, false, "update");
        EventResourceAuthorityRequest create = new(scope.TenantAId, scope.EventAId, userId, false, "create");
        var facts = await reader.ReadBatchAsync([update, create], new(Now), default);
        var route = new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default");
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(route);
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<EventResourceProviderInput>>()
                .Select(_ => EventResourceProviderDecision.Allow).ToArray());
        var service = new EventResourceAuthorityOrchestrator(new EfCoreUnitOfWork(context), reader, routes, provider, new AuthorityClock());
        var outcomes = await service.AuthorizeCapabilitiesAsync([update, create]);
        await Assert.That(outcomes).IsEquivalentTo([EventResourceAuthorityOutcome.Allowed, EventResourceAuthorityOutcome.NotFound]);
        await Assert.That(facts[0]!.Access.GovernancePolicy).IsEqualTo(disabled);
    }
}
