using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Moderation.EventRoleAuthorityCeilingNativeTests.CeilingFixture;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventOwnershipTransferMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Replacement_and_revocation_commit_with_one_audit_and_qualification_revision(bool reusePendingOwner)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var old = await context.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        Guid? replacementId = null;
        if (reusePendingOwner)
        {
            var pending = EventRoleAssignment.Create(fixture.TenantId, fixture.EventId, fixture.Database.SubjectId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Pending, DateTime.UtcNow.AddHours(-1), null, fixture.UserId);
            context.EventRoleAssignments.Add(pending);
            await context.SaveChangesAsync();
            replacementId = pending.Id;
        }
        long revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await using var session = await CreateSession(fixture);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new TransferEventOwnershipCommandHandler(new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            new UserRepository(context), new EventAuthoritySnapshotService(context), new AuditLogRepository(context), metrics,
            NullLogger<TransferEventOwnershipCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database,
            session.Provider, session.Boundary);
        var result = await handler.ExecuteAsync(new TransferEventOwnershipCommand
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            CurrentOwnerAssignmentId = old.Id,
            NewOwnerUserId = fixture.Database.SubjectId,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await Assert.That(result.IsSuccess).IsTrue();
        if (replacementId is { } id) await Assert.That(result.Id).IsEqualTo(id);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().CountAsync(row => row.EventId == fixture.EventId
            && row.UserId == fixture.Database.SubjectId && row.Status == EventRoleAssignmentStatus.Active)).IsEqualTo(1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task An_ineffective_replacement_cannot_leave_a_saved_assignment_or_advance_qualification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var old = await context.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await using var session = await CreateSession(fixture);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new TransferEventOwnershipCommandHandler(new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            new UserRepository(context), new EventAuthoritySnapshotService(context), new AuditLogRepository(context), metrics,
            NullLogger<TransferEventOwnershipCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database,
            session.Provider, session.Boundary);
        var result = await handler.ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            CurrentOwnerAssignmentId = old.Id,
            NewOwnerUserId = fixture.Database.SubjectId,
            StartsAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.EventId == fixture.EventId && row.UserId == fixture.Database.SubjectId)).IsEqualTo(0);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Commit_rechecks_the_original_authority_end_even_after_owned_revocation(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var until = DateTime.UtcNow.AddMinutes(1);
        var old = await context.EventRoleAssignments.SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        old.ExpiresAtUtc = until;
        await context.SaveChangesAsync();
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var clock = new TransferClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        await using var session = await CreateSession(fixture, clock, shared?.Authority(context, clock));
        var auditId = Guid.CreateVersion7();
        bool privacyOwned = false;
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await session.Boundary.ExecuteAsync(fixture.TenantId, fixture.EventId, old.Id, fixture.Database.SubjectId, auditId,
                async (actor, token) =>
                {
                    privacyOwned = context.IdentityFenceOwnsTransaction;
                    var current = await context.EventRoleAssignments.SingleAsync(row => row.Id == old.Id, token);
                    current.Revoke(actor, clock.GetUtcNow().UtcDateTime);
                    await context.SaveChangesAsync(token);
                    clock.Now = new DateTimeOffset(until, TimeSpan.Zero);
                    return BaseCommandResponse.Success(current.Id);
                }, CancellationToken.None);
        });
        await Assert.That(privacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static Task<Session> CreateSession(Fixture fixture, TimeProvider? clock = null, IPrivacyIdentityFenceAuthority? privacy = null)
        => CreateSession(fixture, fixture.Context, clock, privacy);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Replacement_expiry_during_qualification_save_rolls_back_the_complete_transfer(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync();
        var until = DateTime.UtcNow.AddMinutes(1);
        var clock = new TransferClock { Now = new DateTimeOffset(until.AddSeconds(-1), TimeSpan.Zero) };
        var checkpoint = new ExpireReplacementOnQualificationSave(clock, new DateTimeOffset(until, TimeSpan.Zero));
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(fixture.Context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(checkpoint).Options;
        await using var context = new Explore.Persistence.ExploreDbContext(options) { TenantContext = fixture.Database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var old = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        long revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await using var session = await CreateSession(fixture, context, clock, shared?.Authority(context, clock));
        checkpoint.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new TransferEventOwnershipCommandHandler(new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            new UserRepository(context), new EventAuthoritySnapshotService(context), new AuditLogRepository(context), metrics,
            NullLogger<TransferEventOwnershipCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database,
            session.Provider, session.Boundary);
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await handler.ExecuteAsync(new()
            {
                TenantId = fixture.TenantId,
                EventId = fixture.EventId,
                CurrentOwnerAssignmentId = old.Id,
                NewOwnerUserId = fixture.Database.SubjectId,
                StartsAtUtc = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAtUtc = until
            });
        });
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.EventId == fixture.EventId && row.UserId == fixture.Database.SubjectId)).IsEqualTo(0);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, false)]
    public async Task Lost_commit_acknowledgement_recovers_both_assignments_and_the_original_audit_once(bool verify, bool rollback, bool retry)
    {
        await using var fixture = await Fixture.CreateAsync();
        var old = await fixture.Context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollback) { Armed = false };
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(fixture.Context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new Explore.Persistence.ExploreDbContext(options.Options) { TenantContext = fixture.Database };
        long revision = await context.ModerationQualificationEpochs.AsNoTracking().Select(row => row.Revision).SingleAsync();
        await using var session = await CreateSession(fixture, context);
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new TransferEventOwnershipCommandHandler(new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            new UserRepository(context), new EventAuthoritySnapshotService(context), new AuditLogRepository(context), metrics,
            NullLogger<TransferEventOwnershipCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database,
            session.Provider, session.Boundary);
        var request = new TransferEventOwnershipCommand
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            CurrentOwnerAssignmentId = old.Id,
            NewOwnerUserId = fixture.Database.SubjectId,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        if (rollback && !retry)
        {
            await Assert.ThrowsAsync<TenantUserRoleGrantMutationBoundaryTests.LostRoleCommitException>(async () => { await handler.ExecuteAsync(request); });
            await Assert.That(fault.Injected).IsTrue();
            await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
                .IsEqualTo(EventRoleAssignmentStatus.Active);
            await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.EventId == fixture.EventId && row.UserId == fixture.Database.SubjectId)).IsEqualTo(0);
            await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
            return;
        }
        var result = await handler.ExecuteAsync(request);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.EventId == fixture.EventId
            && row.UserId == fixture.Database.SubjectId && row.RoleId == (int)RoleEnum.EventOwner)).IsEqualTo(1);
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_blocks_transfer_before_primary_identity_cleanup(bool actorErased)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var old = await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId && row.RoleId == (int)RoleEnum.EventOwner);
        await fixture.Database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, actorErased ? fixture.UserId : fixture.Database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = await CreateSession(fixture);
        var entered = false;
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            await session.Boundary.ExecuteAsync(fixture.TenantId, fixture.EventId, old.Id, fixture.Database.SubjectId, Guid.CreateVersion7(),
                (_, _) => { entered = true; return Task.FromResult(BaseCommandResponse.Success(old.Id)); }, CancellationToken.None);
        });
        await Assert.That(entered).IsFalse();
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EventRoleAssignmentStatus.Active);
    }

    [Test]
    public async Task A_rolled_back_commit_conflict_reconciles_before_replaying_the_complete_transfer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var old = await fixture.Context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.EventId == fixture.EventId
            && row.RoleId == (int)RoleEnum.EventOwner);
        var fault = new ConflictBeforeTransferCommit();
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(fixture.Context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
        await using var context = new Explore.Persistence.ExploreDbContext(options) { TenantContext = fixture.Database };
        long revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await using var session = await CreateSession(fixture, context);
        fault.Armed = true;
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new TransferEventOwnershipCommandHandler(new EventRoleAssignmentRepository(context, session.PrivateManagement), new EventRepository(context),
            new UserRepository(context), new EventAuthoritySnapshotService(context), new AuditLogRepository(context), metrics,
            NullLogger<TransferEventOwnershipCommandHandler>.Instance, session.Identity, session.Machine, fixture.Database,
            session.Provider, session.Boundary);

        var result = await handler.ExecuteAsync(new()
        {
            TenantId = fixture.TenantId,
            EventId = fixture.EventId,
            CurrentOwnerAssignmentId = old.Id,
            NewOwnerUserId = fixture.Database.SubjectId,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == old.Id)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(await context.EventRoleAssignments.CountAsync(row => row.EventId == fixture.EventId
            && row.UserId == fixture.Database.SubjectId && row.RoleId == (int)RoleEnum.EventOwner)).IsEqualTo(1);
        await Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "EventOwnershipTransferred")).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class ConflictBeforeTransferCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Injected && eventData.Context is Explore.Persistence.ExploreDbContext context
                && context.ChangeTracker.Entries<AuditLog>().Any(entry => entry.Entity.Action == "EventOwnershipTransferred"))
            {
                Injected = true;
                throw new Microsoft.Data.Sqlite.SqliteException("A competing ownership writer prevented this commit.", 5);
            }
            return ValueTask.FromResult(result);
        }
    }

    internal static Task<Session> CreateSession(Fixture fixture, Explore.Persistence.ExploreDbContext context,
        TimeProvider? clock = null, IPrivacyIdentityFenceAuthority? privacy = null,
        Func<CancellationToken, Task>? beforeAuthorize = null)
    {
        clock ??= TimeProvider.System;
        var provider = new Provider(context, beforeAuthorize);
        var human = new CurrentHumanPrincipalFixture(fixture.UserId, fixture.TenantId, borrowedContext: context,
            configureServices: services => services.AddScoped<IPrivacyIdentityFenceAuthority>(_ =>
                privacy ?? fixture.Database.Privacy.Authority(context, clock)), timeProvider: clock);
        // Install ambient request custody synchronously before returning the initialization task.
        var initialization = human.InitializeAsync();
        return CompleteAsync();

        async Task<Session> CompleteAsync()
        {
            try
            {
                await initialization;
                await human.PrepareAsync();
                return new(new EventRoleAssignmentMutationBoundary(context,
                    human.Services.GetRequiredService<IPrivacyIdentityFenceAuthority>(),
                    human.Services.GetRequiredService<IUserContext>(), human.Admin, human.Machine, provider, clock,
                    ModerationRecognitionResourceFixture.Reader(context), human.Services.GetRequiredService<IHumanActionCommitFinalizer>()),
                    human, provider);
            }
            catch
            {
                await human.DisposeAsync();
                throw;
            }
        }
    }

    internal sealed record Session(EventRoleAssignmentMutationBoundary Boundary, CurrentHumanPrincipalFixture Human, Provider Provider) : IAsyncDisposable
    {
        internal IAdminContext Identity => Human.Admin;
        internal MachinePrincipalAccessor Machine => Human.Machine;
        internal IHumanEventTeamActionPreparation Preparation => Human.Services.GetRequiredService<IHumanEventTeamActionPreparation>();
        internal Func<IPrivateEventManagementReader> PrivateManagement => () => Human.Services.GetRequiredService<IPrivateEventManagementReader>();
        internal ISerializableAttemptOwner Owner => Human.Services.GetRequiredService<ISerializableAttemptOwner>();
        public ValueTask DisposeAsync() => Human.DisposeAsync();
    }
    private sealed class TransferClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ExpireReplacementOnQualificationSave(TransferClock clock, DateTimeOffset until) : SaveChangesInterceptor
    {
        private bool _pending;
        public bool Armed { get; set; }
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Observed && eventData.Context is Explore.Persistence.ExploreDbContext context
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
    internal sealed class Provider(Explore.Persistence.ExploreDbContext context,
        Func<CancellationToken, Task>? beforeAuthorize = null) : IAuthorizationProvider
    {
        public async Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            if (context.Database.CurrentTransaction is not null) throw new InvalidOperationException("Provider calls must precede the primary write.");
            if (beforeAuthorize is not null) await beforeAuthorize(cancellationToken);
            return AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local);
        }
        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
