using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Fixture = Event.Persistence.IntegrationTests.Moderation.EventRoleAuthorityCeilingNativeTests.CeilingFixture;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventRoleAssignmentWindowMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_window_change_commits_its_assignment_audit_and_epoch_without_requiring_immediate_effectiveness(bool pending)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var assignment = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.Database.SubjectId,
            (int)RoleEnum.CheckInStaff, pending ? EventRoleAssignmentStatus.Pending : EventRoleAssignmentStatus.Active,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(1), fixture.UserId);
        context.EventRoleAssignments.Add(assignment);
        await context.SaveChangesAsync();
        long revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var starts = DateTime.UtcNow.AddDays(2);
        var expires = starts.AddHours(4);
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId, EventId = fixture.EventId, AssignmentId = assignment.Id,
            StartsAtUtc = starts, ExpiresAtUtc = expires
        });

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == assignment.Id);
        await Assert.That(saved.StartsAtUtc).IsEqualTo(starts);
        await Assert.That(saved.ExpiresAtUtc).IsEqualTo(expires);
        await Assert.That(saved.Status).IsEqualTo(pending ? EventRoleAssignmentStatus.Pending : EventRoleAssignmentStatus.Active);
        await Assert.That(saved.Version).IsEqualTo(assignment.Version + 1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentWindowUpdated")).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Extending_ones_own_window_cannot_replace_the_original_authority_deadline(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var until = DateTime.UtcNow.AddMinutes(1);
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        var manager = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventManager);
        manager.ExpiresAtUtc = until;
        await baseline.SaveChangesAsync();
        long revision = await baseline.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var clock = new WindowClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        var checkpoint = new ExpireAuthorityOnEpochSave(clock, new DateTimeOffset(until, TimeSpan.Zero));
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(checkpoint).Options;
        await using var context = new Explore.Persistence.ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context, clock, shared?.Authority(context, clock));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await Handler(fixture, context, session, metrics).ExecuteAsync(new()
            {
                TenantId = fixture.TenantId, EventId = fixture.EventId, AssignmentId = manager.Id,
                StartsAtUtc = manager.StartsAtUtc, ExpiresAtUtc = until.AddDays(10)
            });
        });
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == manager.Id)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(until);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentWindowUpdated")).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task A_lost_window_commit_acknowledgement_recovers_one_version_audit_and_epoch(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var assignment = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.Database.SubjectId,
            (int)RoleEnum.CheckInStaff, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddHours(-1), null, fixture.UserId);
        baseline.EventRoleAssignments.Add(assignment);
        await baseline.SaveChangesAsync();
        long revision = await baseline.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false);
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new Explore.Persistence.ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var expires = DateTime.UtcNow.AddHours(3);
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId, EventId = fixture.EventId, AssignmentId = assignment.Id,
            StartsAtUtc = assignment.StartsAtUtc, ExpiresAtUtc = expires
        });
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == assignment.Id);
        await Assert.That(saved.ExpiresAtUtc).IsEqualTo(expires);
        await Assert.That(saved.Version).IsEqualTo(assignment.Version + 1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentWindowUpdated")).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task A_denied_window_with_a_lost_acknowledgement_recovers_its_original_failure_once(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.Context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId
            && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await fixture.Context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false);
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(fixture.Context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new Explore.Persistence.ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId, EventId = fixture.EventId, AssignmentId = assignment.Id,
            StartsAtUtc = assignment.StartsAtUtc, ExpiresAtUtc = DateTime.UtcNow.AddHours(3)
        });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentWindowUpdateDenied")).IsEqualTo(1);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == assignment.Id)
            .Select(row => row.Version).SingleAsync()).IsEqualTo(assignment.Version);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task An_indefinite_management_grant_cannot_mask_expiry_of_the_original_delegation_permissions(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        var manager = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventManager);
        manager.ExpiresAtUtc = null;
        await baseline.SaveChangesAsync();
        int management = await baseline.Permissions.Where(row => row.MasterCode == PermissionCodes.EventManageTeam).Select(row => row.Id).SingleAsync();
        await new RoleRepository(baseline).ReplacePermissionsAsync((int)RoleEnum.EventManager, [management], fixture.UserId);
        var until = DateTime.UtcNow.AddMinutes(1);
        var assignment = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.UserId,
            (int)RoleEnum.CheckInStaff, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddHours(-1), until, fixture.UserId);
        baseline.EventRoleAssignments.Add(assignment);
        await baseline.SaveChangesAsync();
        long revision = await baseline.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var clock = new WindowClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        var checkpoint = new ExpireAuthorityOnEpochSave(clock, new DateTimeOffset(until, TimeSpan.Zero));
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(checkpoint).Options;
        await using var context = new Explore.Persistence.ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context, clock, shared?.Authority(context, clock));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await Handler(fixture, context, session, metrics).ExecuteAsync(new()
            {
                TenantId = fixture.TenantId, EventId = fixture.EventId, AssignmentId = assignment.Id,
                StartsAtUtc = assignment.StartsAtUtc, ExpiresAtUtc = until.AddDays(10)
            });
        });
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == assignment.Id)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(until);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentWindowUpdated")).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static UpdateEventRoleAssignmentWindowCommandHandler Handler(Fixture fixture, Explore.Persistence.ExploreDbContext context,
        EventOwnershipTransferMutationTests.Session session, BusinessMetrics metrics) => new(
            new EventRoleAssignmentRepository(context), new EventRepository(context),
            new Explore.Application.Authorization.EventRoleAuthorityCeilingService(
                new Explore.Persistence.Services.EventAuthoritySnapshotService(context), new RoleRepository(context), new PermissionRepository(context)),
            new AuditLogRepository(context), metrics, NullLogger<UpdateEventRoleAssignmentWindowCommandHandler>.Instance,
            session.Identity, session.Machine, fixture.Database, session.Provider, session.Boundary);

    internal sealed class WindowClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class ExpireAuthorityOnEpochSave(WindowClock clock, DateTimeOffset until) : SaveChangesInterceptor
    {
        private bool _pending;
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is Explore.Persistence.ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationQualificationEpoch>().Any(entry => entry.State == EntityState.Modified))
            {
                _pending = true;
                PrivacyOwned = context.IdentityFenceOwnsTransaction;
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending) { _pending = false; Observed = true; clock.Now = until; }
            return ValueTask.FromResult(result);
        }
    }
}
