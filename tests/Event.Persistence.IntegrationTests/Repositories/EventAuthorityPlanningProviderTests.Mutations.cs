using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.Moderation;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class EventAuthorityPlanningProviderTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task An_actor_source_save_can_commit_while_the_assignment_owner_waits_before_its_actor_anchor(bool creating)
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        using var timeout = new CancellationTokenSource(Deadline);
        var token = timeout.Token;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = $"window-source-{Guid.CreateVersion7():N}",
            FullName = "Window source ordering",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var publisher = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"publisher-{Guid.CreateVersion7():N}@example.test", FirstName = "Publisher", LastName = "Witness" }
        };
        var manager = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"manager-{Guid.CreateVersion7():N}@example.test", FirstName = "Manager", LastName = "Witness" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = publisher.Id,
            User = publisher,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Before actor source update" }
        };
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            OrganizerActorId = actor.Id,
            Title = "Window source ordering",
            PublicCode = Guid.CreateVersion7().ToString("N")[..12],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC"
        };
        var now = DateTime.UtcNow;
        var target = EventRoleAssignment.Create(tenant.Id, entity.Id, manager.Id, (int)RoleEnum.CheckInStaff,
            EventRoleAssignmentStatus.Active, now.AddHours(-1), now.AddDays(1), manager.Id);
        await using (var seed = fixture.CreateSystemContext())
        {
            var management = await seed.Permissions.SingleOrDefaultAsync(row => row.MasterCode == PermissionCodes.EventManageTeam, token);
            if (management is null)
            {
                management = new Permission
                {
                    Id = (await seed.Permissions.MaxAsync(row => (int?)row.Id, token) ?? 0) + 1,
                    MasterCode = PermissionCodes.EventManageTeam,
                    FullName = "Manage event team",
                    ResourceKind = "event",
                    Action = "manage-team",
                    GroupName = "Events",
                    Scope = RoleScopeEnum.Event,
                    IsActive = true,
                    IsFiltered = false
                };
                seed.Permissions.Add(management);
            }
            var role = new Role
            {
                Id = (await seed.Roles.MaxAsync(row => (int?)row.Id, token) ?? 0) + 1,
                MasterCode = $"event.window-witness-{Guid.CreateVersion7():N}",
                FullName = "Window witness",
                Scope = RoleScopeEnum.Event
            };
            seed.AddRange(actor, manager, entity, role);
            if (!creating) seed.Add(target);
            seed.Add(EventRoleAssignment.Create(tenant.Id, entity.Id, manager.Id, role.Id,
                EventRoleAssignmentStatus.Active, now.AddHours(-1), now.AddDays(1), manager.Id));
            await seed.SaveChangesAsync(token);
            int[] required = await seed.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff && row.Permission.IsActive)
                .Select(row => row.PermissionId).ToArrayAsync(token);
            await new RoleRepository(seed).AssignPermissionsAsync(role.Id, required.Append(management.Id).Distinct().ToArray(), manager.Id);
        }

        var beforeWriterTenant = new BeforeFence(typeof(Tenant));
        var beforeOwnerActor = new BeforeFence(typeof(Actor));
        await using var writer = fixture.CreateTenantContext(tenant.Id, beforeWriterTenant);
        await using var owner = fixture.CreateTenantContext(tenant.Id, beforeOwnerActor);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        var provider = new EventOwnershipTransferMutationTests.Provider(owner);
        await using var human = new CurrentHumanPrincipalFixture(manager.Id, tenant.Id, borrowedContext: owner,
            configureServices: services => services.AddSingleton<IPrivacyIdentityFenceAuthority>(
                privacy.Authority(owner, TimeProvider.System)).AddSingleton<IAuthorizationProvider>(provider),
            timeProvider: TimeProvider.System,
            purposeConfiguration: fixture.Provider == PrimaryDatabaseProvider.PostgreSql ? fixture.PurposeConfiguration : null,
            seedBinding: fixture.Provider == PrimaryDatabaseProvider.PostgreSql ? async binding =>
            {
                await using var migrator = fixture.CreateMigratorContext();
                migrator.UserExternalLogins.Add(binding);
                await migrator.SaveChangesAsync(token);
            }
        : null);
        await human.InitializeAsync();
        await human.PrepareAsync(token);
        var identity = human.Admin;
        var machine = human.Machine;
        var boundary = new EventRoleAssignmentMutationBoundary(owner, human.Services.GetRequiredService<IPrivacyIdentityFenceAuthority>(),
            human.Services.GetRequiredService<Explore.Application.Contracts.Identity.IUserContext>(), identity, machine, provider,
            TimeProvider.System, ModerationRecognitionResourceFixture.Reader(owner),
            human.Services.GetRequiredService<IHumanActionCommitFinalizer>());
        var metrics = human.Services.GetRequiredService<BusinessMetrics>();
        IPrivateEventManagementReader PrivateReader() => human.Services.GetRequiredService<IPrivateEventManagementReader>();
        var assignments = new EventRoleAssignmentRepository(owner, PrivateReader);
        var events = new EventRepository(owner, privateManagement: PrivateReader);
        var ceiling = new EventRoleAuthorityCeilingService(new EventAuthoritySnapshotService(owner, PrivateReader),
            new RoleRepository(owner), new PermissionRepository(owner));
        var handler = new UpdateEventRoleAssignmentWindowCommandHandler(assignments, events,
            ceiling,
            new AuditLogRepository(owner), metrics, NullLogger<UpdateEventRoleAssignmentWindowCommandHandler>.Instance,
            identity, machine, owner.TenantContext!, provider, boundary);
        var creator = new AssignEventRoleCommandHandler(assignments, events, new UserRepository(owner),
            human.Services.GetRequiredService<IHumanEventTeamActionPreparation>(),
            new AuditLogRepository(owner), metrics, NullLogger<AssignEventRoleCommandHandler>.Instance,
            identity, machine, owner.TenantContext!, provider, boundary);
        Task write = new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
        {
            var source = await writer.Actors.Include(row => row.Pii).SingleAsync(row => row.Id == actor.Id, ct);
            source.Pii!.DisplayName = "After actor source update";
            await writer.SaveChangesAsync(ct);
            return true;
        }, token);
        Task<Explore.Application.Responses.BaseCommandResponse<Guid>>? change = null;
        try
        {
            await beforeWriterTenant.Reached.WaitAsync(Deadline, token);
            change = creating ? creator.ExecuteAsync(new()
            {
                TenantId = tenant.Id,
                EventId = entity.Id,
                TargetUserId = manager.Id,
                RoleId = (int)RoleEnum.CheckInStaff,
                StartsAtUtc = now.AddHours(1),
                ExpiresAtUtc = now.AddDays(2),
                Status = EventRoleAssignmentStatus.Pending
            }, token) : handler.ExecuteAsync(new()
            {
                TenantId = tenant.Id,
                EventId = entity.Id,
                AssignmentId = target.Id,
                StartsAtUtc = target.StartsAtUtc,
                ExpiresAtUtc = now.AddDays(2)
            }, token);
            await beforeOwnerActor.Reached.WaitAsync(Deadline, token);
            beforeWriterTenant.Release();
            // The real Actor save must commit before this owner is allowed to issue its Actor command.
            await write.WaitAsync(Deadline, token);
        }
        finally
        {
            beforeWriterTenant.Release();
            beforeOwnerActor.Release();
            await Task.WhenAll(write, (Task?)change ?? Task.CompletedTask).WaitAsync(Deadline);
        }
        await Assert.That((await change!).IsSuccess).IsTrue();
        await using var current = fixture.CreateTenantContext(tenant.Id);
        await Assert.That((await current.Actors.Include(row => row.Pii).SingleAsync(row => row.Id == actor.Id, token)).Pii!.DisplayName)
            .IsEqualTo("After actor source update");
        string action = creating ? "EventRoleAssignmentCreated" : "EventRoleAssignmentWindowUpdated";
        await Assert.That(await current.AuditLogs.CountAsync(row => row.TenantId == tenant.Id && row.Action == action, token)).IsEqualTo(1);
    }
}
