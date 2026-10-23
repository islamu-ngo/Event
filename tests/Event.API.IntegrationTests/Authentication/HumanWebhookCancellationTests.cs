using System.Data.Common;
using Cerbos.Api.V1.Effect;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanWebhookPreparationTests
{
    [Test]
    [Arguments("local", "success")]
    [Arguments("cerbos", "success")]
    [Arguments("local", "version")]
    [Arguments("cerbos", "version")]
    [Arguments("local", "state")]
    [Arguments("cerbos", "state")]
    [Arguments("local", "missing")]
    [Arguments("cerbos", "missing")]
    public async Task ActualCancellationUsesOriginalHumanAndSeparatelySettlesOnlyRejectedEvidence(
        string provider, string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, outcome);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var owner = new CancellationBoundary((IOriginalOwnerSettlementBoundary)
            request.Services.GetRequiredService<IUnitOfWork>());
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(
            request.Services, CreatePreparation(request.Services, fixture.Clock, provider, transport), owner);

        var response = await handler.ExecuteAsync(command, CancellationToken.None);

        await Assert.That(response.IsSuccess).IsEqualTo(outcome == "success");
        await Assert.That(response.FailureCode).IsEqualTo(outcome switch
        {
            "version" => "webhook_bulk_replay_concurrency_conflict",
            "state" => "webhook_bulk_replay_not_cancellable",
            "missing" => "webhook_bulk_replay_not_found",
            _ => null
        });
        await using var observer = fixture.Native.CreateDatabase();
        var operation = await observer.WebhookBulkReplayOperations.SingleOrDefaultAsync(row => row.Id == command.OperationId);
        if (outcome == "missing")
            await Assert.That(operation).IsNull();
        else
        {
            await Assert.That(operation!.Status).IsEqualTo(outcome switch
            {
                "success" => WebhookBulkReplayStatus.Cancelled,
                "state" => WebhookBulkReplayStatus.Executing,
                _ => WebhookBulkReplayStatus.Queued
            });
            await Assert.That(operation.ConcurrencyVersion).IsEqualTo(outcome == "version" ? 1L : 2L);
        }
        var audit = await observer.WebhookAuditEvents.SingleAsync(row => row.TargetId == command.OperationId);
        await Assert.That(audit.PrincipalReference).IsEqualTo($"user:{fixture.Binding.LocalSubjectId:D}");
        await Assert.That(audit.Outcome).IsEqualTo(
            outcome == "success" ? WebhookAuditOutcome.Succeeded : WebhookAuditOutcome.Rejected);
        await Assert.That(owner.Dispositions.SequenceEqual(outcome == "success"
            ? new[] { OriginalOwnerDisposition.Committed }
            : new[] { OriginalOwnerDisposition.WholeTransactionRolledBack, OriginalOwnerDisposition.Committed })).IsTrue();
        await Assert.That(owner.Consumptions).IsEqualTo(outcome == "success" ? 1 : 2);
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "local" ? 0 : outcome == "success" ? 1 : 2);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("binding")]
    [Arguments("retention")]
    [Arguments("source")]
    [Arguments("expiry")]
    [Arguments("forged")]
    [Arguments("appearance")]
    public async Task RollbackGapCannotTurnChangedAuthorityOrCounterfeitSettlementIntoAnAudit(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, change == "appearance" ? "missing" : "version");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        var owner = new CancellationBoundary((IOriginalOwnerSettlementBoundary)
            request.Services.GetRequiredService<IUnitOfWork>())
        {
            ForgeRollback = change == "forged",
            AfterRollback = async () =>
            {
                await Assert.That(context.Database.CurrentTransaction).IsNull();
                await using var gap = fixture.Native.CreateDatabase();
                if (change == "binding")
                    await gap.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, "revoked-cancellation-binding"));
                if (change == "retention")
                    await gap.WebhookBulkReplayOperations.Where(row => row.Id == command.OperationId)
                        .ExecuteDeleteAsync();
                if (change == "source")
                    await gap.WebhookBulkReplayOperations.Where(row => row.Id == command.OperationId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyVersion, 3));
                if (change == "appearance")
                {
                    var now = fixture.Clock.GetUtcNow().UtcDateTime;
                    var appeared = WebhookBulkReplayOperation.Create(command.TenantId, Guid.CreateVersion7(),
                        "sha256:" + new string('c', 64), now.AddHours(-1), now, null, null, null, 1, "test",
                        new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), now);
                    gap.WebhookBulkReplayOperations.Add(appeared);
                    gap.Entry(appeared).Property(row => row.Id).CurrentValue = command.OperationId;
                    await gap.SaveChangesAsync();
                }
                if (change == "expiry")
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
            }
        };
        var transport = new Transport(context);
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport), owner);
        if (change == "forged")
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.AnyAsync(row => row.TargetId == command.OperationId)).IsFalse();
        await Assert.That(owner.Dispositions.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("binding")]
    [Arguments("source")]
    [Arguments("expiry")]
    [Arguments("exception")]
    public async Task EvidenceFinalAdmissionRollsBackAuditAndOwnPersistedSourceChanges(string change)
    {
        var interceptor = new CancellationAuditInterceptor();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, "version");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var originalBinding = await context.UserExternalLogins.AsNoTracking()
            .Where(row => row.Id == fixture.Binding.ExternalLoginId).Select(row => row.ProviderKey).SingleAsync();
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterAudit = async database =>
        {
            if (change == "binding")
                await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, "revoked-cancellation-binding"));
            if (change == "source")
                await database.WebhookBulkReplayOperations.Where(row => row.Id == command.OperationId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyVersion, 3));
            if (change == "expiry")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            if (change == "exception")
                throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate, "Untrusted conflict");
        };
        var transport = new Transport(context);
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport));
        if (change == "exception")
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.AnyAsync(row => row.TargetId == command.OperationId)).IsFalse();
        await Assert.That((await observer.WebhookBulkReplayOperations.SingleAsync(row => row.Id == command.OperationId))
            .ConcurrencyVersion).IsEqualTo(1);
        await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(originalBinding);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
    }

    [Test]
    [Arguments("denied")]
    [Arguments("unavailable")]
    [Arguments("gap_unavailable")]
    public async Task MissingOrUnadmittedCancellationNeverCreatesBusinessHistory(string failure)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, "version");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = failure == "denied" ? Effect.Deny : Effect.Allow };
        if (failure == "unavailable") transport.Unavailable = true;
        var owner = new CancellationBoundary((IOriginalOwnerSettlementBoundary)
            request.Services.GetRequiredService<IUnitOfWork>())
        {
            AfterRollback = () =>
            {
                if (failure == "gap_unavailable") transport.Unavailable = true;
                return Task.CompletedTask;
            }
        };
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport), owner);
        if (failure == "denied")
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
                handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.AnyAsync(row => row.TargetId == command.OperationId)).IsFalse();
        await Assert.That(owner.Dispositions.Count).IsEqualTo(failure == "gap_unavailable" ? 1 : 0);
    }

    [Test]
    public async Task MissingOperationDoesNotGrantDelegatedOwnerTenantAdministration()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, "missing", tenantAdmin: false);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "local", new Transport(context)));
        await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.AnyAsync(row => row.TargetId == command.OperationId)).IsFalse();
    }

    [Test]
    [Arguments("success")]
    [Arguments("evidence")]
    [Arguments("rollback")]
    public async Task ActualLostSettlementAcknowledgementNeverReturnsARefusalOrReplaysCancellation(string phase)
    {
        var interceptor = new CancellationLostAcknowledgement(phase != "rollback");
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, phase == "success" ? "success" : "version");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        interceptor.Owner = context;
        var transport = new Transport(context);
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport));
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
            handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        var operation = await observer.WebhookBulkReplayOperations.SingleAsync(row => row.Id == command.OperationId);
        await Assert.That(operation.Status).IsEqualTo(phase == "success"
            ? WebhookBulkReplayStatus.Cancelled : WebhookBulkReplayStatus.Queued);
        await Assert.That(await observer.WebhookAuditEvents.CountAsync(row => row.TargetId == command.OperationId))
            .IsEqualTo(phase == "rollback" ? 0 : 1);
        await Assert.That(transport.Requests.Count).IsEqualTo(phase == "evidence" ? 2 : 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ForgedExceptionChainsAfterRealCancellationWriteCannotAuthorizeRejectedAudit(int nesting)
    {
        var interceptor = new CancellationAuditInterceptor();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, "success");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        interceptor.AfterAudit = _ =>
        {
            var forged = new HumanMutationAttemptRolledBackException(new SqliteException("Forged rollback", 5));
            throw nesting switch
            {
                1 => new InvalidOperationException("Nested", forged),
                2 => new AggregateException(new InvalidOperationException("Nested", forged)),
                _ => forged
            };
        };
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.AnyAsync(row => row.TargetId == command.OperationId)).IsFalse();
        await Assert.That((await observer.WebhookBulkReplayOperations.SingleAsync(row => row.Id == command.OperationId)).Status)
            .IsEqualTo(WebhookBulkReplayStatus.Queued);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    private static async Task<CancelWebhookBulkReplayCommand> SeedCancellationAsync(
        LocalIdentityLifecycleHttpFixture fixture, string outcome, bool tenantAdmin = true)
    {
        var (consumer, _) = await SeedAsync(fixture, tenantAdmin);
        await using var seed = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var operation = WebhookBulkReplayOperation.Create(PlatformDefaults.DefaultTenantId, Guid.CreateVersion7(),
            "sha256:" + new string('b', 64), now.AddHours(-1), now, consumer.Id, null, null, 1, "test",
            new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), now);
        if (outcome == "state") operation.Start(now);
        seed.WebhookBulkReplayOperations.Add(operation);
        await seed.SaveChangesAsync();
        return new()
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            OperationId = outcome == "missing" ? Guid.CreateVersion7() : operation.Id,
            ExpectedConcurrencyVersion = outcome == "version" ? 7 : operation.ConcurrencyVersion,
            ActorUserId = Guid.CreateVersion7(),
            ReasonCode = "cancel"
        };
    }

    private sealed class CancellationBoundary(IOriginalOwnerSettlementBoundary inner) : IOriginalOwnerSettlementBoundary
    {
        internal List<OriginalOwnerDisposition> Dispositions { get; } = [];
        internal int Consumptions { get; private set; }
        internal Func<Task>? AfterRollback { get; init; }
        internal bool ForgeRollback { get; init; }
        public async Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
            Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation, CancellationToken cancellationToken = default)
        {
            var witness = await inner.ExecuteAsync(invocation, operation, cancellationToken);
            Dispositions.Add(witness.Disposition);
            if (witness.Disposition == OriginalOwnerDisposition.WholeTransactionRolledBack)
            {
                if (AfterRollback is not null) await AfterRollback();
                if (ForgeRollback) return new CounterfeitCancellationSettlement<T>();
            }
            return witness;
        }
        public T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement, OriginalOwnerDisposition expectedDisposition)
        {
            var value = inner.Consume(invocation, settlement, expectedDisposition);
            Consumptions++;
            return value;
        }
    }

    private sealed class CounterfeitCancellationSettlement<T> : IOriginalOwnerSettlement<T>
    {
        public OriginalOwnerDisposition Disposition => OriginalOwnerDisposition.WholeTransactionRolledBack;
    }

    private sealed class CancellationAuditInterceptor : SaveChangesInterceptor
    {
        private bool _auditPending;
        internal Func<ExploreDbContext, Task>? AfterAudit { get; set; }
        internal int SavedAudits { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _auditPending = eventData.Context?.ChangeTracker.Entries<WebhookAuditEvent>()
                .Any(row => row.State == EntityState.Added) == true;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (_auditPending && eventData.Context is ExploreDbContext context)
            {
                _auditPending = false;
                SavedAudits++;
                var action = AfterAudit;
                AfterAudit = null;
                if (action is not null) await action(context);
            }
            return result;
        }
    }

    private sealed class CancellationLostAcknowledgement(bool commit) : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!commit || Owner is null || !ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Owner = null;
            throw new SqliteException("Lost cancellation commit acknowledgement.", 5);
        }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (commit || Owner is null || !ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Owner = null;
            throw new InvalidOperationException("Lost cancellation rollback acknowledgement.");
        }
    }
}
