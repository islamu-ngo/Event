using System.Data.Common;
using System.Transactions;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task ReceiptCommitterReturnsOnlyPersistedUnconsumedAccountBoundReceipt(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        await using var context = database.Context();
        var committer = IssuanceCommitter(context, database, database.Caller);
        var issued = await committer.IssueSubmissionReceiptAsync(database.Request(), default);
        await Assert.That(issued).IsNotNull();
        await Assert.That(issued!.Token.Length).IsEqualTo(32);
        await Assert.That(issued.Receipt.CallerUserId).IsEqualTo(database.CallerId);
        await Assert.That(issued.Receipt.ConsumedAt).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        var persisted = await verify.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == issued.Receipt.Id);
        await Assert.That(persisted.TokenDigest).IsEqualTo(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(issued.Token.Span)));
        await Assert.That(await verify.ModerationCaseTargetSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync()).IsFalse();
        // The same scoped issuer must discard its old transaction fences before fresh issuance.
        var fresh = await committer.IssueSubmissionReceiptAsync(database.Request(), default);
        await Assert.That(fresh).IsNotNull();
        await Assert.That(fresh!.Receipt.Id).IsNotEqualTo(issued.Receipt.Id);
        await Assert.That(fresh.Receipt.SourceId).IsEqualTo(issued.Receipt.SourceId);
    }

    [Test]
    [Arguments("target")]
    [Arguments("relationship")]
    [Arguments("revoked")]
    public async Task ReceiptCommitterRefusesWrongSourceAndDeniedRelationshipWithoutPersistingToken(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Resource);
        var request = database.Request();
        await using var context = database.Context();
        if (defect == "target") request = request with { NativeTargetId = database.FormId };
        else if (defect == "relationship") request = request with { RelationshipSourceId = database.OrderId };
        else
            await context.EventRoleAssignments.Where(row => row.Id == database.RoleId).ExecuteDeleteAsync();
        var committer = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(await committer.IssueSubmissionReceiptAsync(request, default)).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("current")]
    [Arguments("expiry")]
    [Arguments("role")]
    public async Task MintedReceiptMustRemainQualifiedAfterNativeDisclosureFinalization(string boundary)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        long? before;
        await using (var original = database.Context())
            before = await original.Set<EventDiscoveryRevision>().AsNoTracking()
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        DateTimeOffset? expiry = null;
        var finalization = new BudgetDisclosureFinalizationCheckpoint(database.TenantId, before, async context =>
        {
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            if (boundary == "expiry") database.Clock.Now = expiry!.Value;
            if (boundary == "role")
                await context.EventRoleAssignments.Where(row => row.Id == database.RoleId).ExecuteDeleteAsync();
        });
        var saved = new ReceiptSavedCheckpoint(async context =>
        {
            expiry = (await context.ModerationCaseTargetReceipts.SingleAsync()).ExpiresAt;
            context.DisclosureMutations.Enlist([database.TenantId]);
            finalization.Armed = true;
        });
        await using (var context = database.Context(saved, finalization))
        {
            var issued = await IssuanceCommitter(context, database, database.Caller)
                .IssueSubmissionReceiptAsync(database.Request(), default);
            await Assert.That(issued is not null).IsEqualTo(boundary == "current");
            await Assert.That(saved.ObservedReceiptSave).IsTrue();
            await Assert.That(finalization.ObservedAdvancement).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        }
        await using var verify = database.Context();
        var accepted = boundary == "current";
        await Assert.That(await verify.ModerationCaseTargetReceipts.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await verify.ModerationCaseTargetSources.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await verify.EventRoleAssignments.AnyAsync(row => row.Id == database.RoleId)).IsTrue();
        var after = await verify.Set<EventDiscoveryRevision>().AsNoTracking()
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(after).IsEqualTo(accepted ? before.GetValueOrDefault() + 1 : before);
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("native")]
    [Arguments("account")]
    [Arguments("role")]
    [Arguments("policy")]
    [Arguments("expiry")]
    [Arguments("machine")]
    [Arguments("caller")]
    [Arguments("authentication")]
    [Arguments("missing-caller")]
    [Arguments("empty-caller")]
    [Arguments("tenant")]
    [Arguments("missing-tenant")]
    [Arguments("bypass")]
    [Arguments("tracked")]
    public async Task ReceiptCommitterRollsBackWhenOriginalAuthorityChangesAfterSave(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var caller = database.Caller;
        var checkpoint = new ReceiptSavedCheckpoint(async context =>
        {
            switch (defect)
            {
                case "native":
                    await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                    break;
                case "account":
                    await context.Users.Where(row => row.Id == database.CallerId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.IsDeleted, true));
                    break;
                case "role":
                    await context.EventRoleAssignments.Where(row => row.Id == database.RoleId).ExecuteDeleteAsync();
                    break;
                case "policy":
                    await context.ModerationCaseTargetPolicies.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.Enabled, false));
                    break;
                case "expiry": database.Clock.Now = database.Clock.Now.AddMinutes(5); break;
                case "machine":
                    database.Machine.Current = new ApiKeyPrincipalContext($"test-{Guid.CreateVersion7():N}",
                        database.TenantId, ExternalApiKeyOwnerType.User, database.CallerId, []);
                    break;
                case "caller": caller.UserId = Guid.CreateVersion7(); break;
                case "authentication": caller.IsAuthenticated = false; break;
                case "missing-caller": caller.UserId = null; break;
                case "empty-caller": caller.UserId = Guid.Empty; break;
                case "tenant": context.TenantContext = new SelectedTenant(Guid.CreateVersion7()); break;
                case "missing-tenant": context.TenantContext = null; break;
                case "bypass": context.EnableTenantFilterBypass("Receipt issuance authority-loss invariant."); break;
                case "tracked": context.Users.Add(new User { Id = Guid.CreateVersion7(), Pii = null! }); break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        });
        await using var context = database.Context(checkpoint);
        var committer = IssuanceCommitter(context, database, caller);
        await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
        await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.Users.AnyAsync(row => row.Id == database.CallerId)).IsTrue();
    }

    [Test]
    [Arguments("machine")]
    [Arguments("authentication")]
    [Arguments("missing-caller")]
    [Arguments("empty-caller")]
    [Arguments("tenant")]
    [Arguments("missing-tenant")]
    [Arguments("bypass")]
    [Arguments("tracked")]
    [Arguments("trust")]
    public async Task ReceiptCommitterRefusesUnownedContextWithoutClearingForeignWork(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        await using var context = database.Context();
        var caller = database.Caller;
        var committer = IssuanceCommitter(context, database, caller);
        var foreign = new User { Id = Guid.CreateVersion7(), Pii = null! };
        switch (defect)
        {
            case "machine":
                database.Machine.Current = new ApiKeyPrincipalContext($"test-{Guid.CreateVersion7():N}",
                    database.TenantId, ExternalApiKeyOwnerType.User, database.CallerId, []);
                break;
            case "authentication": caller.IsAuthenticated = false; break;
            case "missing-caller": caller.UserId = null; break;
            case "empty-caller": caller.UserId = Guid.Empty; break;
            case "tenant": context.TenantContext = new SelectedTenant(Guid.CreateVersion7()); break;
            case "missing-tenant": context.TenantContext = null; break;
            case "bypass": context.EnableTenantFilterBypass("Receipt issuance foreign-context invariant."); break;
            case "tracked": context.Users.Add(foreign); break;
            case "trust": context.ModerationTrustDomainId = database.TrustId; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
        if (defect == "tracked")
            await Assert.That(context.Entry(foreign).State).IsEqualTo(EntityState.Added);
        if (defect == "trust")
            await Assert.That(context.ModerationTrustDomainId).IsEqualTo(database.TrustId);
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ReceiptCommitterRefusesCallerOwnedTransactionWithoutDisposingIt()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        await using var context = database.Context();
        var committer = IssuanceCommitter(context, database, database.Caller);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ReceiptCommitterRefusesAmbientTransactionBeforeProviderEnlistment()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        await using (var context = database.Context())
        {
            var committer = IssuanceCommitter(context, database, database.Caller);
            using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ModerationTrustDomainId).IsNull();
        }
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ReceiptCommitterOwnsBothMaterialBuffersBeforeOpeningTransaction()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.LocalIncident);
        var expected = await database.IssueAsync();
        var request = database.Request();
        var occurrence = request.OccurrenceMaterial.ToArray();
        var material = request.Material.ToArray();
        request = request with { OccurrenceMaterial = occurrence, Material = material };
        var checkpoint = new ReceiptTransactionStartedCheckpoint(_ =>
        {
            Array.Clear(occurrence);
            Array.Clear(material);
        });
        await using var context = database.Context(checkpoint);
        var committer = IssuanceCommitter(context, database, database.Caller);
        var issued = await committer.IssueSubmissionReceiptAsync(request, default);
        await Assert.That(issued).IsNotNull();
        await Assert.That(checkpoint.ObservedTransactionStart).IsTrue();
        await Assert.That(issued!.Receipt.SourceId).IsEqualTo(expected.Receipt.SourceId);
        await Assert.That(issued.Receipt.MaterialFactId).IsEqualTo(expected.Receipt.MaterialFactId);
        await Assert.That(occurrence.All(value => value == 0)).IsTrue();
        await Assert.That(material.All(value => value == 0)).IsTrue();
    }

    [Test]
    public async Task ReceiptCommitterRefusesForeignTrackedWorkIntroducedWhileOpeningTransaction()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var foreignId = Guid.CreateVersion7();
        var checkpoint = new ReceiptTransactionStartedCheckpoint(context =>
            context.Users.Add(new User
            {
                Id = foreignId, Pii = new UserPii
                {
                    UserId = foreignId, Email = $"{foreignId:N}@example.invalid",
                    FirstName = "Foreign", LastName = "Work"
                }
            }));
        await using var context = database.Context(checkpoint);
        var committer = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
        await Assert.That(checkpoint.ObservedTransactionStart).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.Users.AnyAsync(row => row.Id == foreignId)).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ReceiptCommitterCancelsAfterSaveWithoutRetainingWritesOrTransactionContext()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new ReceiptSavedCheckpoint(_ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        await using var context = database.Context(checkpoint);
        var committer = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(() => committer.IssueSubmissionReceiptAsync(database.Request(), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReceiptCommitterNeverRetriesMintOrPublishesAfterUncertainCommit(bool commitReachedDatabase)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var checkpoint = new ReceiptCommitFailureCheckpoint(commitReachedDatabase);
        await using var context = database.RetryingContext(checkpoint);
        var committer = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        await Assert.That(await committer.IssueSubmissionReceiptAsync(database.Request(), default)).IsNull();
        await Assert.That(checkpoint.CommitAttempts).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.CountAsync())
            .IsEqualTo(commitReachedDatabase ? 1 : 0);
        await Assert.That(await verify.ModerationCaseTargetSources.CountAsync())
            .IsEqualTo(commitReachedDatabase ? 1 : 0);
    }

    private static ModerationCaseIntakeCommitter IssuanceCommitter(
        ExploreDbContext context, NativeDatabase database, NativeCaller caller)
    {
        context.ModerationTrustDomainId = null;
        context.CurrentUserService = caller;
        var owner = new ModerationCaseTargetProofOwner(context, caller, database.Machine, database.Clock);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, caller, database.Machine, database.Clock),
            owner, database.Clock);
        return new(context, intake, owner, caller, database.Machine, database.Clock,
            new ModerationCaseNarrativeRepository(context, NarrativeAdmin(context, caller), caller, database.Machine,
                intake, database.Clock, database.NarrativeProtection), NarrativeAdmin(context, caller));
    }

    private sealed class ReceiptSavedCheckpoint(Func<ExploreDbContext, Task> afterSave) : SaveChangesInterceptor
    {
        public bool ObservedReceiptSave { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedReceiptSave && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationCaseTargetReceipt>().Any())
            {
                ObservedReceiptSave = true;
                await afterSave(context);
            }
            return result;
        }
    }

    private sealed class ReceiptTransactionStartedCheckpoint(Action<ExploreDbContext> afterStart) : DbTransactionInterceptor
    {
        public bool ObservedTransactionStart { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            ObservedTransactionStart = true;
            afterStart((ExploreDbContext)eventData.Context!);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReceiptCommitFailureCheckpoint(bool afterCommit) : DbTransactionInterceptor
    {
        public int CommitAttempts { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            if (!afterCommit) throw new SqliteException("Injected commit rejection.", 5);
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            throw new SqliteException("Injected lost commit acknowledgement.", 5);
    }

    private sealed class ReceiptRetryingStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ReceiptRetryingStrategy(dependencies);
    }

    private sealed class ReceiptRetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException;
    }
}
