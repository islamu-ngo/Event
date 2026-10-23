using Explore.Application.Authorization;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Fixture = Event.Persistence.IntegrationTests.Moderation.EventRoleAuthorityCeilingNativeTests.CeilingFixture;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventRoleAssignmentRevocationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Revocation_commits_the_target_audit_and_qualification_together(bool pending)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var assignment = await AddTargetAsync(fixture, pending);
        long revision = await EpochAsync(context);
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            AssignmentId = assignment.Id
        });
        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == assignment.Id);
        await Assert.That(saved.Status).IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(saved.RevokedByUserId).IsEqualTo(fixture.UserId);
        await Assert.That(saved.Version).IsEqualTo(assignment.Version + 1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevoked")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Lost_self_revocation_acknowledgement_recovers_the_original_success(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        await baseline.SaveChangesAsync();
        var manager = await baseline.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventManager);
        long revision = await EpochAsync(baseline);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false) { Armed = false };
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            AssignmentId = manager.Id
        });
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == manager.Id);
        await Assert.That(saved.Status).IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(saved.Version).IsEqualTo(manager.Version + 1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevoked")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Direct_owner_revocation_recovers_a_denial_without_changing_ownership_or_epoch(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await EpochAsync(baseline);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false) { Armed = false };
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            AssignmentId = owner.Id
        });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_owner_transfer_required");
        await Assert.That(fault.Injected).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == owner.Id);
        await Assert.That(saved.Status).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(saved.Version).IsEqualTo(owner.Version);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevokeDenied")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_denies_the_actor_but_does_not_veto_target_privilege_removal(bool actorErased)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var assignment = await AddTargetAsync(fixture, false);
        long revision = await EpochAsync(context);
        await fixture.Database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, actorErased ? fixture.UserId : fixture.Database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        async Task RevokeAsync()
        {
            var result = await Handler(fixture, context, session, metrics).ExecuteAsync(new()
            {
                TenantId = fixture.TenantId,
                EventId = fixture.EventId,
                AssignmentId = assignment.Id
            });
            await Assert.That(result.IsSuccess).IsTrue();
        }
        if (actorErased) await Assert.ThrowsAsync<AuthorizationException>(RevokeAsync);
        else await RevokeAsync();
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == assignment.Id)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(actorErased ? EventRoleAssignmentStatus.Active : EventRoleAssignmentStatus.Revoked);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision + (actorErased ? 0 : 1));
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Self_revocation_does_not_erase_the_original_authority_deadline_at_actual_commit(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        var manager = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventManager);
        var until = DateTime.UtcNow.AddMinutes(1);
        manager.ExpiresAtUtc = until;
        await baseline.SaveChangesAsync();
        long revision = await EpochAsync(baseline);
        var clock = new EventRoleAssignmentWindowMutationTests.WindowClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        var checkpoint = new EventRoleAssignmentWindowMutationTests.ExpireAuthorityOnEpochSave(clock, new DateTimeOffset(until, TimeSpan.Zero));
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(checkpoint).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context, clock, shared?.Authority(context, clock));
        checkpoint.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await Handler(fixture, context, session, metrics).ExecuteAsync(new()
            {
                TenantId = fixture.TenantId,
                EventId = fixture.EventId,
                AssignmentId = manager.Id
            });
        });
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == manager.Id)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevoked")).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Indefinite_management_cannot_mask_the_original_delegation_ceiling_at_revocation_commit(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        await baseline.SaveChangesAsync();
        int management = await baseline.Permissions.Where(row => row.MasterCode == PermissionCodes.EventManageTeam).Select(row => row.Id).SingleAsync();
        await new RoleRepository(baseline).ReplacePermissionsAsync((int)RoleEnum.EventManager, [management], fixture.UserId);
        var until = DateTime.UtcNow.AddMinutes(1);
        var target = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.UserId,
            (int)RoleEnum.CheckInStaff, EventRoleAssignmentStatus.Active, until.AddHours(-1), until, fixture.UserId);
        baseline.EventRoleAssignments.Add(target);
        await baseline.SaveChangesAsync();
        long revision = await EpochAsync(baseline);
        var clock = new EventRoleAssignmentWindowMutationTests.WindowClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        var checkpoint = new EventRoleAssignmentWindowMutationTests.ExpireAuthorityOnEpochSave(clock, new DateTimeOffset(until, TimeSpan.Zero));
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(checkpoint).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context, clock, shared?.Authority(context, clock));
        checkpoint.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await Handler(fixture, context, session, metrics).ExecuteAsync(new()
            {
                TenantId = fixture.TenantId,
                EventId = fixture.EventId,
                AssignmentId = target.Id
            });
        });
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == target.Id)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevoked")).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task A_rolled_back_denial_does_not_publish_an_extra_committed_denial()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await EpochAsync(baseline);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(true, true) { Armed = false };
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault)
            .ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>().Options;
        await using var context = new ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context);
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var log = new DenialLog();
        var result = await Handler(fixture, context, session, metrics, log).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            AssignmentId = owner.Id
        });
        await Assert.That(result.FailureCode).IsEqualTo("event_owner_transfer_required");
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(log.Denials).IsEqualTo(1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevokeDenied")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task A_target_removed_after_rollback_cannot_publish_the_discarded_denial_reason()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await EpochAsync(baseline);
        var fault = new ConflictBeforeDenialSave();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = fixture.Database };
        bool removed = false;
        await using var session = await EventOwnershipTransferMutationTests.CreateSession(fixture, context,
            beforeAuthorize: async token =>
            {
                if (fault.Injected && !removed)
                {
                    removed = true;
                    await context.EventRoleAssignments.Where(row => row.Id == owner.Id).ExecuteDeleteAsync(token);
                }
            });
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var log = new DenialLog();
        var result = await Handler(fixture, context, session, metrics, log).ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            AssignmentId = owner.Id
        });
        await Assert.That(result.FailureCode).IsEqualTo("event_role_assignment_not_found");
        await Assert.That(removed).IsTrue();
        await Assert.That(log.Denials).IsEqualTo(0);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentRevokeDenied")).IsEqualTo(0);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class ConflictBeforeDenialSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Injected && eventData.Context is ExploreDbContext context && context.ChangeTracker.Entries<AuditLog>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Action == "EventRoleAssignmentRevokeDenied"))
            {
                Injected = true;
                throw new Microsoft.Data.Sqlite.SqliteException("A competing writer prevented the denial audit save.", 5);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DenialLog : ILogger<RevokeEventRoleAssignmentCommandHandler>
    {
        public int Denials { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Denials++;
        }
    }

    private static async Task<EventRoleAssignment> AddTargetAsync(Fixture fixture, bool pending)
    {
        var assignment = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.Database.SubjectId,
            (int)RoleEnum.CheckInStaff, pending ? EventRoleAssignmentStatus.Pending : EventRoleAssignmentStatus.Active,
            DateTime.UtcNow.AddHours(-1), null, fixture.UserId);
        fixture.Context.EventRoleAssignments.Add(assignment);
        await fixture.Context.SaveChangesAsync();
        return assignment;
    }
    private static Task<long> EpochAsync(ExploreDbContext context) => context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
    private static RevokeEventRoleAssignmentCommandHandler Handler(Fixture fixture, ExploreDbContext context,
        EventOwnershipTransferMutationTests.Session session, BusinessMetrics metrics,
        ILogger<RevokeEventRoleAssignmentCommandHandler>? logger = null) => new(
            new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            session.Preparation, new AuditLogRepository(context), metrics,
            logger ?? NullLogger<RevokeEventRoleAssignmentCommandHandler>.Instance, session.Identity, session.Machine,
            fixture.Database, session.Provider, session.Boundary);
}
