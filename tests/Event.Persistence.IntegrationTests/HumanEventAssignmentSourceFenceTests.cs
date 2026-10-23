using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanEventAssignmentSourceFenceTests
{
    private static readonly DateTime Start = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PersistedAssignmentsMatchOriginalUnsortedAuthorityAtInclusiveStartAndExclusiveExpiry()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        var assignment = EventRoleAssignment.Create(fixture.TenantId, parent.Id, fixture.UserId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Start, Start.AddHours(1), fixture.UserId);
        fixture.Context.EventRoleAssignments.Add(assignment);
        await fixture.Context.SaveChangesAsync();
        var source = new HumanEventAssignmentSourceFence(fixture.Context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant, fixture.TenantId, [parent.Id, Guid.CreateVersion7()]);
        var facts = await source.ObserveAsync();
        foreach (DateTime time in new[] { Start.AddTicks(-1), Start, Start.AddHours(1) })
        {
            var original = await new EventAuthoritySnapshotService(fixture.Context).GetForUserAndEventsAsync(
                fixture.TenantId, fixture.UserId, facts.EventIds, time, default);
            var projected = HumanEventAssignmentProjection.Authority(facts, time);
            foreach (Guid id in facts.EventIds)
            {
                await Assert.That(projected.Events[id].RoleCodes.SequenceEqual(original.Events[id].RoleCodes)).IsTrue();
                await Assert.That(projected.Events[id].PermissionCodes.SequenceEqual(original.Events[id].PermissionCodes)).IsTrue();
                await Assert.That(projected.Events[id].IsOwner).IsEqualTo(original.Events[id].IsOwner);
                await Assert.That(projected.Events[id].IsManager).IsEqualTo(original.Events[id].IsManager);
            }
        }
        await Assert.That(HumanEventAssignmentProjection.Authority(facts, Start).Events[parent.Id].IsOwner).IsTrue();
        await Assert.That(HumanEventAssignmentProjection.Authority(facts, Start.AddHours(1)).Events[parent.Id].IsOwner).IsFalse();
        await Assert.That(HumanEventAssignmentProjection.Resource(facts, parent.Id)!.ActorId).IsEqualTo(fixture.ActorId);
        await Assert.That(HumanEventAssignmentProjection.ControlsOrganizer(facts, parent.Id)).IsTrue();
    }

    [Test]
    public async Task FinalFactsSeeOwnPersistedWritesButNeverUnflushedTrackedAssignments()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        var context = fixture.Context;
        var subject = await SubjectAsync(fixture);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var source = new HumanEventAssignmentSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
            fixture.TenantId, [parent.Id]);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        var prepared = await source.ObserveAsync();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var setLease = await sets.AcquireAsync();
        var principalLease = await principal.AcquireAsync(setLease);
        var held = await source.AcquireAsync(setLease);
        var assignment = EventRoleAssignment.Create(fixture.TenantId, parent.Id, fixture.UserId,
            (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Start, Start.AddHours(1), fixture.UserId);
        context.EventRoleAssignments.Add(assignment);
        await Assert.That((await held.ValidateAsync()).Assignments.Length).IsEqualTo(0);
        await context.SaveChangesAsync();
        await principalLease.ValidateAsync();
        var final = await held.ValidateAsync();
        await Assert.That(prepared.Assignments.Length).IsEqualTo(0);
        await Assert.That(held.Facts.Assignments.Length).IsEqualTo(0);
        await Assert.That(final.Assignments.Single().Id).IsEqualTo(assignment.Id);
        await Assert.That(HumanEventAssignmentProjection.Authority(final, Start).Events[parent.Id].IsManager).IsTrue();
        await context.EventRoleAssignments.Where(row => row.Id == assignment.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, EventRoleAssignmentStatus.Revoked));
        var revoked = await held.ValidateAsync();
        await Assert.That(HumanEventAssignmentProjection.Authority(revoked, Start).Events[parent.Id].IsManager).IsFalse();
        await owner.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await replacement.RollbackAsync();
    }

    [Test]
    public async Task OrganizerPlacementSuspensionAndEligibilityAreFreshWithoutAdministratorShortcut()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        var context = fixture.Context;
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Assignment source" },
            CreatedAt = Start,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var placement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsOrganizerEligible = true,
            CreatedAt = Start,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            OrganizationId = organization.Id,
            Pii = new ActorPii { DisplayName = "Assignment source" },
            CreatedAt = Start
        };
        context.AddRange(organization, placement, actor, new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            OrganizationTenantId = placement.Id,
            OrganizationTenant = placement,
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = Start
        });
        await context.SaveChangesAsync();
        await context.Events.Where(row => row.Id == parent.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.OrganizerActorId, actor.Id));
        var permission = await context.Permissions.SingleAsync(row => row.MasterCode == PermissionCodes.EventCreate);
        if (!await context.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.OrgAdmin && row.PermissionId == permission.Id))
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!,
                PermissionId = permission.Id,
                Permission = null!
            });
            await context.SaveChangesAsync();
        }
        var source = new HumanEventAssignmentSourceFence(context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant, fixture.TenantId, [parent.Id]);
        await Assert.That(HumanEventAssignmentProjection.ControlsOrganizer(await source.ObserveAsync(), parent.Id)).IsTrue();
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await context.OrganizationTenants.Where(row => row.Id == placement.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true));
        await Assert.That(HumanEventAssignmentProjection.ControlsOrganizer(await held.ValidateAsync(), parent.Id)).IsFalse();
        await context.OrganizationTenants.Where(row => row.Id == placement.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, false)
                .SetProperty(row => row.IsOrganizerEligible, false));
        await Assert.That(HumanEventAssignmentProjection.ControlsOrganizer(await held.ValidateAsync(), parent.Id)).IsFalse();
        await owner.RollbackAsync();
    }

    [Test]
    public async Task EmptyAssignmentSetStillExcludesAnotherSqliteWriterAndObservationRejectsTransactions()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        var source = new HumanEventAssignmentSourceFence(fixture.Context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant, fixture.TenantId, [parent.Id]);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false
        }.ToString());
        await writer.OpenAsync();
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
        await Assert.That((await held.ValidateAsync()).Assignments.Length).IsEqualTo(0);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task DeletedCreatingActorRemovesResourceWithoutChangingOrdinaryAssignmentAuthority()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        fixture.Context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.TenantId, parent.Id, fixture.UserId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Start, null, fixture.UserId));
        await fixture.Context.SaveChangesAsync();
        var source = new HumanEventAssignmentSourceFence(fixture.Context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant, fixture.TenantId, [parent.Id]);
        await fixture.Context.Actors.Where(row => row.Id == fixture.ActorId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        var facts = await source.ObserveAsync();
        await Assert.That(facts.Actors.Single().IsDeleted).IsTrue();
        await Assert.That(await new Explore.Persistence.Repositories.EventRepository(fixture.Context)
            .GetEventWithDetails(parent.Id)).IsNull();
        await Assert.That(HumanEventAssignmentProjection.Resource(facts, parent.Id)).IsNull();
        await Assert.That(HumanEventAssignmentProjection.Authority(facts, Start).Events[parent.Id].IsOwner).IsTrue();
    }

    [Test]
    public async Task InactivePermissionAndRevocationTimestampPreserveOriginalOrdinarySnapshotRules()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        var assignment = EventRoleAssignment.Create(fixture.TenantId, parent.Id, fixture.UserId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Start, null, fixture.UserId);
        fixture.Context.EventRoleAssignments.Add(assignment);
        await fixture.Context.SaveChangesAsync();
        await fixture.Context.EventRoleAssignments.Where(row => row.Id == assignment.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAtUtc, Start));
        var permission = await fixture.Context.Permissions.SingleAsync(row => row.MasterCode == PermissionCodes.EventManageTeam);
        if (!await fixture.Context.RolePermissions.AnyAsync(row =>
            row.RoleId == (int)RoleEnum.EventOwner && row.PermissionId == permission.Id))
        {
            fixture.Context.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.EventOwner,
                Role = null!,
                PermissionId = permission.Id,
                Permission = null!
            });
            await fixture.Context.SaveChangesAsync();
        }
        var source = new HumanEventAssignmentSourceFence(fixture.Context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant, fixture.TenantId, [parent.Id]);
        var active = await source.ObserveAsync();
        await Assert.That(HumanEventAssignmentProjection.Authority(active, Start).Events[parent.Id].IsOwner).IsTrue();
        await Assert.That(HumanEventAssignmentProjection.Authority(active, Start).Events[parent.Id].IsManager).IsTrue();
        await fixture.Context.Permissions.Where(row => row.Id == permission.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false));
        var inactive = await source.ObserveAsync();
        await Assert.That(inactive.Assignments.Single().RevokedAtUtc).IsEqualTo(Start);
        await Assert.That(HumanEventAssignmentProjection.Authority(inactive, Start).Events[parent.Id].IsOwner).IsTrue();
        await Assert.That(HumanEventAssignmentProjection.Authority(inactive, Start).Events[parent.Id].IsManager).IsFalse();
    }

    private static async Task<HumanCommitSubject> SubjectAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        await using var authenticated = new CurrentHumanPrincipalFixture(
            fixture.UserId, fixture.TenantId, fixture.DatabasePath);
        await authenticated.InitializeAsync();
        return await HumanCommitSubject.ResolveAsync(
            authenticated.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            authenticated.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
            ?? throw new InvalidOperationException("A persisted human is required.");
    }
}
