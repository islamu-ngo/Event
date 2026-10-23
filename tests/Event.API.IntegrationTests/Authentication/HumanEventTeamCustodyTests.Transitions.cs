using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanEventTeamCustodyTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("filtered")]
    [Arguments("requested_role")]
    [Arguments("requested_permission")]
    [Arguments("binding")]
    [Arguments("stamp")]
    [Arguments("event")]
    [Arguments("route")]
    [Arguments("assignment_window")]
    public async Task AssignmentRejectsDriftAcrossEveryOriginalAuthorityContributor(string change)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        var identity = await fixture.ReadIdentityAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        string roleName;
        int permissionId;
        await using (var original = fixture.Native.CreateDatabase())
        {
            roleName = await original.Roles.Where(row => row.Id == (int)RoleEnum.CheckInStaff).Select(row => row.FullName).SingleAsync();
            permissionId = await original.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff)
                .Select(row => row.PermissionId).FirstAsync();
        }
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            await Assert.That(await context.EventRoleAssignments.AsNoTracking().AnyAsync(row =>
                row.EventId == target.Event && row.RoleId == (int)RoleEnum.CheckInStaff, token)).IsTrue();
            switch (change)
            {
                case "filtered":
                    await context.Permissions.Where(row => row.Id == permissionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsFiltered, true), token);
                    break;
                case "requested_role":
                    await context.Roles.Where(row => row.Id == (int)RoleEnum.CheckInStaff)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "Changed requested role"), token);
                    break;
                case "requested_permission":
                    await context.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff
                        && row.PermissionId == permissionId).ExecuteDeleteAsync(token);
                    break;
                case "binding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.CreateVersion7().ToString("D")), token);
                    break;
                case "stamp":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("D")), token);
                    break;
                case "event":
                    await context.Events.Where(row => row.Id == target.Event)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()), token);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "assignment_window":
                    await context.EventRoleAssignments.Where(row => row.Id == target.Assignment)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                            fixture.Clock.GetUtcNow().AddDays(2).UtcDateTime), token);
                    break;
            }
        });
        var handler = ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services, owner);
        if (change == "unchanged")
            await Assert.That((await handler.ExecuteAsync(Assignment(fixture, target.Event))).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(Assignment(fixture, target.Event)));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().CountAsync(row => row.EventId == target.Event))
            .IsEqualTo(change == "unchanged" ? 2 : 1);
        await Assert.That(await observer.Roles.Where(row => row.Id == (int)RoleEnum.CheckInStaff)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo(roleName);
        await Assert.That(await observer.Permissions.Where(row => row.Id == permissionId).Select(row => row.IsFiltered).SingleAsync()).IsFalse();
        await Assert.That(await observer.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.CheckInStaff && row.PermissionId == permissionId)).IsTrue();
        await Assert.That((await fixture.ReadIdentityAsync()).SecurityStamp).IsEqualTo(identity.SecurityStamp);
        await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityType == nameof(EventRoleAssignment)
            && row.ActorId == fixture.Binding.LocalSubjectId && row.Action == "EventRoleAssignmentCreated"))
            .IsEqualTo(change == "unchanged");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelfRevocationAllowsOnlyItsClosedTransitionNotNewAuthority(bool unrelatedGrant)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await MakeManagerDelegable(fixture);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == target.Assignment)
                .Select(row => row.Status).SingleAsync(token)).IsEqualTo(EventRoleAssignmentStatus.Revoked);
            if (unrelatedGrant)
            {
                context.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId,
                    target.Event, fixture.Binding.LocalSubjectId, (int)RoleEnum.CheckInStaff,
                    EventRoleAssignmentStatus.Active, fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime, null,
                    fixture.Binding.LocalSubjectId));
                await context.SaveChangesAsync(token);
            }
        });
        var handler = ActivatorUtilities.CreateInstance<RevokeEventRoleAssignmentCommandHandler>(request.Services, owner);
        var command = new RevokeEventRoleAssignmentCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            EventId = target.Event,
            AssignmentId = target.Assignment,
            ActorUserId = fixture.Binding.LocalSubjectId
        };
        if (unrelatedGrant) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command));
        else await Assert.That((await handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == target.Assignment);
        await Assert.That(persisted.Status).IsEqualTo(unrelatedGrant ? EventRoleAssignmentStatus.Active : EventRoleAssignmentStatus.Revoked);
        await Assert.That(persisted.Version).IsEqualTo(unrelatedGrant ? 1L : 2L);
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityId == target.Assignment.ToString()
            && row.Action == "EventRoleAssignmentRevoked")).IsEqualTo(!unrelatedGrant);
        if (!unrelatedGrant)
        {
            await Assert.That(persisted.RevokedByUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(persisted.RevokedAtUtc!.Value.Ticks % 10).IsEqualTo(0L);
        }
    }

    [Test]
    [Arguments("success")]
    [Arguments("equal")]
    [Arguments("event")]
    [Arguments("target")]
    [Arguments("replay")]
    [Arguments("scope")]
    public async Task EmailHandoffKeepsItsOriginalRequestAndResolvedTarget(string change)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var original = new AssignEventRoleByEmailCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            EventId = target.Event,
            ActorUserId = fixture.Binding.LocalSubjectId,
            TargetUserEmail = " " + fixture.Login.Identifier + " ",
            RoleId = (int)RoleEnum.CheckInStaff,
            StartsAtUtc = fixture.Clock.GetUtcNow().AddMinutes(-1).UtcDateTime
        };
        await using var foreign = change == "scope" ? new Request(fixture.Host.Services) : null;
        if (foreign is not null)
        {
            await foreign.Authenticate(await fixture.SignInAsync());
            request.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = request.Context;
        }
        if (change == "success")
        {
            var result = await ActivatorUtilities.CreateInstance<AssignEventRoleByEmailCommandHandler>(request.Services)
                .ExecuteAsync(original);
            await Assert.That(result.IsSuccess).IsTrue();
        }
        else
        {
            var prepared = await request.Services.GetRequiredService<IHumanEventTeamActionPreparation>().PrepareAsync(original, default);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
                {
                    await prepared.AcquireAsync(original, token);
                    var inner = prepared.BindResolvedTarget(original, fixture.Binding.LocalSubjectId);
                    var altered = change switch
                    {
                        "equal" => inner with { },
                        "event" => inner with { EventId = Guid.CreateVersion7() },
                        "target" => inner with { TargetUserId = Guid.CreateVersion7() },
                        _ => inner
                    };
                    var body = request.Services.GetRequiredService<IHumanEventTeamAssignmentOwner>();
                    if (change == "scope")
                    {
                        await foreign!.Services.GetRequiredService<IHumanEventTeamAssignmentOwner>()
                            .ExecuteHandoffAsync(inner, prepared, token);
                    }
                    var result = await body.ExecuteHandoffAsync(altered, prepared, token);
                    if (change == "replay")
                    {
                        await Assert.That(result.IsSuccess).IsTrue();
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => body.ExecuteHandoffAsync(inner, prepared, token));
                    }
                    return result;
                }));
        }
        await using var observer = fixture.Native.CreateDatabase();
        var rows = await observer.EventRoleAssignments.AsNoTracking().Where(row => row.EventId == target.Event
            && row.RoleId == (int)RoleEnum.CheckInStaff).ToListAsync();
        await Assert.That(rows.Count).IsEqualTo(change == "success" ? 1 : 0);
        if (change == "success")
        {
            await Assert.That(rows.Single().UserId).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityId == rows.Single().Id.ToString()
                && row.Action == "EventRoleAssignmentCreated")).IsTrue();
        }
    }

    [Test]
    public async Task AuditSaveFailureRollsBackTheAlreadySavedAssignment()
    {
        var failure = new AuditSaveFailure();
        await using var fixture = await Event.Api.IntegrationTests.Fixtures.LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: failure);
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        failure.Armed = true;
        await Assert.ThrowsAsync<IOException>(() =>
            ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services).ExecuteAsync(Assignment(fixture, target.Event)));
        await Assert.That(failure.ObservedPersistedAssignment).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityType == nameof(EventRoleAssignment)
            && row.ActorId == fixture.Binding.LocalSubjectId)).IsFalse();
    }

    private sealed class AuditSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool ObservedPersistedAssignment { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<AuditLog>().FirstOrDefault(entry => entry.State == EntityState.Added
                    && entry.Entity.Action == "EventRoleAssignmentCreated") is { } audit)
            {
                Guid assignment = Guid.Parse(audit.Entity.EntityId);
                ObservedPersistedAssignment = await context.EventRoleAssignments.AsNoTracking()
                    .AnyAsync(row => row.Id == assignment, cancellationToken);
                throw new IOException("Audit persistence failed before settlement.");
            }
            return result;
        }
    }
}
