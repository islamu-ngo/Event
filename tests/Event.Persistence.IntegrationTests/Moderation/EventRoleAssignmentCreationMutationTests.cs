using Explore.Application.Authorization;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
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
using Microsoft.Extensions.Logging.Abstractions;
using Fixture = Event.Persistence.IntegrationTests.Moderation.EventRoleAuthorityCeilingNativeTests.CeilingFixture;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventRoleAssignmentCreationMutationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Both_aliases_commit_the_first_assignment_audit_and_epoch_including_future_windows(bool email, bool pending)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var before = await context.EventRoleAssignments.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Version, row.Status }).ToArrayAsync();
        long revision = await EpochAsync(context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var request = Request(fixture) with { Status = pending ? EventRoleAssignmentStatus.Pending : EventRoleAssignmentStatus.Active };
        var result = await ExecuteAsync(fixture, context, metrics, request, email);
        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == result.Id);
        await Assert.That(saved.UserId).IsEqualTo(fixture.Database.SubjectId);
        await Assert.That(saved.RoleId).IsEqualTo(request.RoleId);
        await Assert.That(saved.StartsAtUtc).IsEqualTo(request.StartsAtUtc);
        await Assert.That(saved.Status).IsEqualTo(request.Status);
        await Assert.That(saved.CreatedBy).IsEqualTo(fixture.UserId);
        await Assert.That(saved.Version).IsEqualTo(1);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id != result.Id)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Version, row.Status }).ToArrayAsync()).IsEquivalentTo(before);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentCreated")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Duplicate_and_owner_denials_do_not_advance_qualification(bool email)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var request = Request(fixture);
        var created = await ExecuteAsync(fixture, context, metrics, request, email);
        long revision = await EpochAsync(context);
        var duplicate = await ExecuteAsync(fixture, context, metrics, request, email);
        var owner = await ExecuteAsync(fixture, context, metrics, request with { RoleId = (int)RoleEnum.EventOwner }, email);
        await Assert.That(created.IsSuccess).IsTrue();
        await Assert.That(duplicate.FailureCode).IsEqualTo("event_role_assignment_duplicate");
        await Assert.That(owner.FailureCode).IsEqualTo("event_owner_transfer_required");
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.UserId == fixture.Database.SubjectId)).IsEqualTo(1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentDenied")).IsEqualTo(1);
    }

    [Test]
    [Arguments((int)EventRoleAssignmentStatus.Revoked)]
    [Arguments(99)]
    public async Task Terminal_or_undefined_creation_status_cannot_write_any_part_of_a_grant(int status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        long revision = await EpochAsync(context);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<ArgumentException>(async () => await ExecuteAsync(fixture, context, metrics,
            Request(fixture) with { Status = (EventRoleAssignmentStatus)status }, false));
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.UserId == fixture.Database.SubjectId)).IsFalse();
        await Assert.That(await context.AuditLogs.AnyAsync()).IsFalse();
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Lost_creation_acknowledgement_returns_the_original_result_not_a_duplicate(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        long revision = await EpochAsync(fixture.Context);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(fixture.Context.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await ExecuteAsync(fixture, context, metrics, Request(fixture), true);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.UserId == fixture.Database.SubjectId && row.Id == result.Id)).IsEqualTo(1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentCreated")).IsEqualTo(1);
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision + 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_of_actor_or_target_blocks_new_authority(bool actor)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        long revision = await EpochAsync(context);
        await fixture.Database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, actor ? fixture.UserId : fixture.Database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () => await ExecuteAsync(fixture, context, metrics, Request(fixture), true));
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.UserId == fixture.Database.SubjectId)).IsFalse();
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Email_change_or_reassignment_during_final_save_rolls_back_the_selected_humans_grant(bool reassigned, bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        string email = await baseline.UserPii.Where(row => row.UserId == fixture.Database.SubjectId).Select(row => row.Email!).SingleAsync();
        long revision = await EpochAsync(baseline);
        var checkpoint = new ChangeEmailAtEpochSave(fixture.Database.SubjectId, fixture.Database.ReviewerId, email, reassigned);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(checkpoint);
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        await using var retained = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () => await ExecuteAsync(fixture, context, metrics, Request(fixture), true,
            privacy: retained?.Authority(context, TimeProvider.System)));
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.UserId == fixture.Database.SubjectId)).IsFalse();
        await Assert.That(await context.AuditLogs.AnyAsync()).IsFalse();
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
        await Assert.That(await context.UserPii.Where(row => row.UserId == fixture.Database.SubjectId).Select(row => row.Email).SingleAsync()).IsEqualTo(email);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_self_grant_cannot_renew_the_original_short_delegation_at_actual_commit(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Context;
        var owner = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        owner.Revoke(fixture.UserId, DateTime.UtcNow);
        var manager = await baseline.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventManager);
        manager.ExpiresAtUtc = null;
        var until = DateTime.UtcNow.AddMinutes(1);
        var supporting = fixture.Assignment(RoleEnum.EventDiscoveryReviewer);
        supporting.ExpiresAtUtc = until;
        baseline.EventRoleAssignments.Add(supporting);
        await baseline.SaveChangesAsync();
        await fixture.Roles.ReplacePermissionsAsync((int)RoleEnum.EventManager, [await fixture.PermissionIdAsync(PermissionCodes.EventManageTeam)]);
        await fixture.Roles.ReplacePermissionsAsync((int)RoleEnum.EventDiscoveryReviewer, await baseline.RolePermissions
            .Where(row => row.RoleId == (int)RoleEnum.CheckInStaff).Select(row => row.PermissionId).ToArrayAsync());
        long revision = await EpochAsync(baseline);
        var clock = new EventRoleAssignmentWindowMutationTests.WindowClock { Now = new DateTimeOffset(until.AddSeconds(-1)) };
        var checkpoint = new EventRoleAssignmentWindowMutationTests.ExpireAuthorityOnEpochSave(clock, new DateTimeOffset(until));
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(checkpoint);
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        await using var retained = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Assert.ThrowsAsync<AuthorizationException>(async () => await ExecuteAsync(fixture, context, metrics,
            Request(fixture) with { TargetUserId = fixture.UserId }, false, clock, retained?.Authority(context, clock)));
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.UserId == fixture.UserId && row.RoleId == (int)RoleEnum.CheckInStaff)).IsFalse();
        await Assert.That(await context.AuditLogs.AnyAsync()).IsFalse();
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task An_uncertain_owner_creation_denial_is_not_replayed_as_another_audit(bool verify, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        long revision = await EpochAsync(fixture.Context);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, false);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(fixture.Context.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var result = await ExecuteAsync(fixture, context, metrics, Request(fixture) with { RoleId = (int)RoleEnum.EventOwner }, true);
        await Assert.That(result.FailureCode).IsEqualTo("event_owner_transfer_required");
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventRoleAssignmentDenied")).IsEqualTo(1);
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.UserId == fixture.Database.SubjectId)).IsFalse();
        await Assert.That(await EpochAsync(context)).IsEqualTo(revision);
    }

    private static AssignEventRoleCommand Request(Fixture fixture) => new()
    {
        TenantId = fixture.TenantId, EventId = fixture.EventId, TargetUserId = fixture.Database.SubjectId,
        RoleId = (int)RoleEnum.CheckInStaff, Status = EventRoleAssignmentStatus.Active,
        StartsAtUtc = DateTime.UtcNow.AddHours(1), ExpiresAtUtc = DateTime.UtcNow.AddDays(2)
    };

    private static async Task<BaseCommandResponse<Guid>> ExecuteAsync(Fixture fixture, ExploreDbContext context,
        BusinessMetrics metrics, AssignEventRoleCommand request, bool email, TimeProvider? clock = null,
        IPrivacyIdentityFenceAuthority? privacy = null)
    {
        var session = EventOwnershipTransferMutationTests.CreateSession(fixture, context, clock, privacy);
        var handler = new AssignEventRoleCommandHandler(new EventRoleAssignmentRepository(context), new EventRepository(context),
            new UserRepository(context), new EventRoleAuthorityCeilingService(new EventAuthoritySnapshotService(context),
                new RoleRepository(context), new PermissionRepository(context)), new AuditLogRepository(context), metrics,
            NullLogger<AssignEventRoleCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database, session.Provider, session.Boundary);
        if (!email) return await handler.ExecuteAsync(request);
        var targetEmail = await context.UserPii.AsNoTracking().Where(row => row.UserId == request.TargetUserId).Select(row => row.Email!).SingleAsync();
        return await new AssignEventRoleByEmailCommandHandler(new UserRepository(context), handler, session.Identity,
            session.Machine, fixture.Database, session.Provider).ExecuteAsync(new()
            {
                TenantId = request.TenantId, EventId = request.EventId, TargetUserEmail = targetEmail,
                RoleId = request.RoleId, Status = request.Status, StartsAtUtc = request.StartsAtUtc, ExpiresAtUtc = request.ExpiresAtUtc
            });
    }

    private static Task<long> EpochAsync(ExploreDbContext context) => context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();

    private sealed class ChangeEmailAtEpochSave(Guid target, Guid successor, string email, bool reassigned) : SaveChangesInterceptor
    {
        private bool _pending;
        public bool Observed { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationQualificationEpoch>().Any(entry => entry.State == EntityState.Modified)) _pending = true;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending && eventData.Context is ExploreDbContext context)
            {
                _pending = false;
                Observed = true;
                await context.UserPii.Where(row => row.UserId == target).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Email, $"changed-{target:N}@example.test"), cancellationToken);
                if (reassigned) await context.UserPii.Where(row => row.UserId == successor).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Email, email), cancellationToken);
            }
            return result;
        }
    }
}
