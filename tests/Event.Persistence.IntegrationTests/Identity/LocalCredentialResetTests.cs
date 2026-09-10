
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalCredentialResetTests
{
    public enum ResetWriteBoundary { AfterFirstWrite = 1, AfterSecondWrite = 2, AfterThirdWrite = 3 }
    public enum CallerWork { PendingChanges, ActiveTransaction }
    private enum InvalidLedgerShape { MissingPredecessor, MissingPredecessorStamp, MissingReason, CreateWithResetMetadata, SelfPredecessor, PendingReset }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.Ready)]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.ChangeRequired)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.Ready)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.ChangeRequired)]
    public async Task ResetRevokesPriorCredentialAndPreservesOriginalVerificationProvenance(
        IdentityDatabaseTopology topology, LocalCredentialState initialState)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology, initialState);
        await fixture.RestrictNativeAccountAsync();
        Snapshot before = await fixture.ReadAsync();
        await Assert.That(before.EmailConfirmed).IsFalse();
        await Assert.That(before.LockoutEnabled && before.LockoutEnd > fixture.Clock.GetUtcNow()).IsTrue();
        LocalCredentialResetRequest request = fixture.Request(before);
        LocalCredentialReplacementAuthority priorAuthority = fixture.Authority(before);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.CurrentPassword)).IsTrue();

        LocalCredentialResetResult result = await fixture.ResetAsync(request);

        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        await Assert.That(string.IsNullOrWhiteSpace(result.TemporaryPassword)).IsFalse();
        await Assert.That(result.Receipt).IsNotNull();
        LocalCredentialResetReceipt receipt = result.Receipt!;
        await Assert.That(receipt.Operation.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(receipt.Operation.Kind).IsEqualTo(LocalCredentialOperationKind.Reset);
        await Assert.That(receipt.Operation.Stage).IsEqualTo(LocalCredentialOperationStage.ChangeRequired);
        await Assert.That(receipt.PreviousOperationId).IsEqualTo(before.Metadata.OperationId);
        await Assert.That(receipt.PreviousOperationConcurrencyStamp).IsEqualTo(before.CurrentOperation.Stamp);
        await Assert.That(receipt.Reason).IsEqualTo(request.Reason);
        await Assert.That(request.ToString().Contains(request.Reason, StringComparison.Ordinal)
            || result.ToString().Contains(result.TemporaryPassword!, StringComparison.Ordinal)
            || receipt.ToString().Contains(request.Reason, StringComparison.Ordinal)).IsFalse();
        Snapshot reset = await fixture.ReadAsync();
        await Assert.That(reset.Metadata.State).IsEqualTo(LocalCredentialState.ChangeRequired);
        await Assert.That(reset.Metadata.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(reset.CurrentOperation.InitiatorId).IsEqualTo(fixture.ResetActorId);
        await Assert.That(reset.CurrentOperation.VerifiedBy).IsEqualTo(fixture.CreatorId);
        await Assert.That(reset.CurrentOperation.VerifiedAt).IsEqualTo(before.CurrentOperation.VerifiedAt);
        await Assert.That(reset.CurrentOperation.CreatedAt > reset.CurrentOperation.VerifiedAt).IsTrue();
        await Assert.That(reset.CurrentOperation.PersonalActorId).IsEqualTo(before.CurrentOperation.PersonalActorId);
        await Assert.That(reset.CurrentOperation.ExternalLoginId).IsEqualTo(before.CurrentOperation.ExternalLoginId);
        await Assert.That(reset.EmailConfirmed).IsEqualTo(before.EmailConfirmed);
        await Assert.That(reset.LockoutEnabled).IsEqualTo(before.LockoutEnabled);
        await Assert.That(reset.LockoutEnd).IsEqualTo(before.LockoutEnd);
        await Assert.That(reset.AccessFailedCount).IsEqualTo(before.AccessFailedCount);
        await Assert.That(reset.Operations.Single(operation => operation.Id == before.Metadata.OperationId).Stage)
            .IsEqualTo(LocalCredentialOperationStage.Superseded);
        await Assert.That(reset.Operations.Count).IsEqualTo(before.Operations.Count + 1);
        await Assert.That(string.Equals(reset.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(reset.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(reset.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.CurrentPassword)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(result.TemporaryPassword!)).IsTrue();
        await using (AsyncServiceScope scope = fixture.Provider.CreateAsyncScope())
        {
            LocalCredentialReplacementSubject? current = await fixture.StateStore(scope).ReadReplacementSubjectAsync(
                localSubjectId: fixture.OriginalReceipt.LocalSubjectId, expectedSecurityStamp: reset.SecurityStamp!,
                cancellationToken: fixture.CancellationToken);
            await Assert.That(current).IsNotNull();
            await Assert.That(current!.OperationId).IsEqualTo(request.OperationId);
            await Assert.That(await fixture.StateStore(scope).ReadReplacementSubjectAsync(
                localSubjectId: fixture.OriginalReceipt.LocalSubjectId, expectedSecurityStamp: before.SecurityStamp!,
                cancellationToken: fixture.CancellationToken)).IsNull();
        }
        if (initialState == LocalCredentialState.ChangeRequired)
        {
            LocalCredentialReplacementOutcome stale = await fixture.ReplaceAsync(authority: priorAuthority, password: NewPassword());
            await Assert.That(stale).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
            await fixture.AssertUnchangedAsync(reset);
        }
        await fixture.AssertExactGraphAsync();

        string privatePassword = NewPassword();
        LocalCredentialReplacementOutcome replaced = await fixture.ReplaceAsync(authority: fixture.Authority(reset), password: privatePassword);

        await Assert.That(replaced).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        Snapshot ready = await fixture.ReadAsync();
        await Assert.That(ready.Metadata.State).IsEqualTo(LocalCredentialState.Ready);
        await Assert.That(ready.CurrentOperation.Kind).IsEqualTo(LocalCredentialOperationKind.Reset);
        await Assert.That(ready.CurrentOperation.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);
        await Assert.That(await fixture.PasswordIsValidAsync(result.TemporaryPassword!)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(privatePassword)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ResetReplayNeverRevealsPlaintextOrOverwritesDivergentAndLaterOperations(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialResetRequest request = fixture.Request(await fixture.ReadAsync());
        LocalCredentialResetResult first = await fixture.ResetAsync(request);
        await Assert.That(first.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        Snapshot committed = await fixture.ReadAsync();

        LocalCredentialResetResult replay = await fixture.ResetAsync(request);

        await Assert.That(replay.Outcome).IsEqualTo(LocalCredentialResetOutcome.Replayed);
        await Assert.That(replay.Receipt!.Operation.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(string.IsNullOrEmpty(replay.TemporaryPassword)).IsTrue();
        await fixture.AssertUnchangedAsync(committed);
        LocalCredentialResetRequest[] divergent =
        [
            new(operationId: request.OperationId, initiatingApplicationUserId: fixture.CreatorId,
                localSubjectId: request.LocalSubjectId, expectedCurrentOperationId: request.ExpectedCurrentOperationId,
                expectedCurrentOperationConcurrencyStamp: request.ExpectedCurrentOperationConcurrencyStamp, reason: request.Reason),
            new(operationId: request.OperationId, initiatingApplicationUserId: request.InitiatingApplicationUserId,
                localSubjectId: Guid.CreateVersion7(), expectedCurrentOperationId: request.ExpectedCurrentOperationId,
                expectedCurrentOperationConcurrencyStamp: request.ExpectedCurrentOperationConcurrencyStamp, reason: request.Reason),
            new(operationId: request.OperationId, initiatingApplicationUserId: request.InitiatingApplicationUserId,
                localSubjectId: request.LocalSubjectId, expectedCurrentOperationId: Guid.CreateVersion7(),
                expectedCurrentOperationConcurrencyStamp: request.ExpectedCurrentOperationConcurrencyStamp, reason: request.Reason),
            new(operationId: request.OperationId, initiatingApplicationUserId: request.InitiatingApplicationUserId,
                localSubjectId: request.LocalSubjectId, expectedCurrentOperationId: request.ExpectedCurrentOperationId,
                expectedCurrentOperationConcurrencyStamp: Guid.CreateVersion7(), reason: request.Reason),
            new(operationId: request.OperationId, initiatingApplicationUserId: request.InitiatingApplicationUserId,
                localSubjectId: request.LocalSubjectId, expectedCurrentOperationId: request.ExpectedCurrentOperationId,
                expectedCurrentOperationConcurrencyStamp: request.ExpectedCurrentOperationConcurrencyStamp, reason: "Different supervised reason")
        ];
        foreach (LocalCredentialResetRequest changed in divergent)
        {
            LocalCredentialResetResult rejected = await fixture.ResetAsync(changed);
            await Assert.That(rejected.Outcome).IsEqualTo(LocalCredentialResetOutcome.Conflict);
            await Assert.That(rejected.Receipt).IsNull();
            await Assert.That(string.IsNullOrEmpty(rejected.TemporaryPassword)).IsTrue();
            await fixture.AssertUnchangedAsync(committed);
        }
        await Assert.That(await fixture.ReplaceAsync(authority: fixture.Authority(committed), password: NewPassword()))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        LocalCredentialResetRequest laterRequest = fixture.Request(await fixture.ReadAsync());
        await Assert.That((await fixture.ResetAsync(laterRequest)).Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        Snapshot later = await fixture.ReadAsync();

        LocalCredentialResetResult historical = await fixture.ResetAsync(request);

        await Assert.That(historical.Outcome).IsEqualTo(LocalCredentialResetOutcome.Replayed);
        await Assert.That(historical.Receipt!.Operation.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(string.IsNullOrEmpty(historical.TemporaryPassword)).IsTrue();
        await fixture.AssertUnchangedAsync(later);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task MissingExactApplicationBindingCannotResetNativeCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        await using (AsyncServiceScope remove = fixture.Provider.CreateAsyncScope())
        {
            var application = remove.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await application.UserExternalLogins.Where(login => login.Id == fixture.OriginalReceipt.ExternalLoginId)
                .ExecuteDeleteAsync(fixture.CancellationToken);
        }

        LocalCredentialResetResult denied = await fixture.ResetAsync(fixture.Request(before));

        await Assert.That(denied.Outcome).IsEqualTo(LocalCredentialResetOutcome.BindingIncomplete);
        await Assert.That(string.IsNullOrEmpty(denied.TemporaryPassword)).IsTrue();
        await fixture.AssertUnchangedAsync(before);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.CurrentPassword)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ResetWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ResetWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ResetWriteBoundary.AfterThirdWrite)]
    [Arguments(IdentityDatabaseTopology.External, ResetWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.External, ResetWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.External, ResetWriteBoundary.AfterThirdWrite)]
    public async Task FailureBetweenResetWritesRollsBackBothReceiptsTokenAndPassword(
        IdentityDatabaseTopology topology, ResetWriteBoundary boundary)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        fixture.WriteFault.Arm(identity: fixture.Identity(request), boundary: boundary);

        await Assert.ThrowsAsync<InjectedResetFailure>(() => fixture.StateStore(request)
            .ResetAsync(fixture.Request(before), fixture.CancellationToken));

        await Assert.That(fixture.WriteFault.Observed).IsTrue();
        await Assert.That(fixture.WriteFault.WritesObserved).IsEqualTo((int)boundary);
        await Assert.That(fixture.WriteFault.AffectedRows).IsEqualTo(1);
        await fixture.AssertUnchangedAsync(before);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.CurrentPassword)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CommittedResetWithLostAcknowledgementReplaysSafeReceiptOnly(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialResetRequest command = fixture.Request(before);
        await using (AsyncServiceScope request = fixture.Provider.CreateAsyncScope())
        {
            fixture.CommitFault.Arm(fixture.Identity(request));
            await Assert.ThrowsAsync<InjectedResetFailure>(() => fixture.StateStore(request).ResetAsync(command, fixture.CancellationToken));
        }
        await Assert.That(fixture.CommitFault.Observed).IsTrue();
        Snapshot committed = await fixture.ReadAsync();
        await Assert.That(committed.Metadata.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(committed.Metadata.State).IsEqualTo(LocalCredentialState.ChangeRequired);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.CurrentPassword)).IsFalse();

        LocalCredentialResetResult replay = await fixture.ResetAsync(command);

        await Assert.That(replay.Outcome).IsEqualTo(LocalCredentialResetOutcome.Replayed);
        await Assert.That(replay.Receipt!.Operation.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(string.IsNullOrEmpty(replay.TemporaryPassword)).IsTrue();
        await fixture.AssertUnchangedAsync(committed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ResetAndPrivateReplacementCompeteForOnePredecessorWithoutResurrectingOldAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialResetRequest request = fixture.Request(before);
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        string privatePassword = NewPassword();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replaceReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LocalCredentialResetResult> resetTask = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            resetReady.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await fixture.StateStore(scope).ResetAsync(request, fixture.CancellationToken);
        }, fixture.CancellationToken);
        Task<LocalCredentialReplacementOutcome> replaceTask = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            replaceReady.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await fixture.StateStore(scope).ReplaceAsync(
                new LocalCredentialReplacementRequest(authority: authority, newPassword: privatePassword), fixture.CancellationToken);
        }, fixture.CancellationToken);
        await Task.WhenAll(resetReady.Task, replaceReady.Task).WaitAsync(fixture.CancellationToken);
        start.SetResult();

        await Task.WhenAll(resetTask, replaceTask).WaitAsync(fixture.CancellationToken);

        LocalCredentialResetResult reset = await resetTask;
        LocalCredentialReplacementOutcome replacement = await replaceTask;
        bool resetWon = reset.Outcome == LocalCredentialResetOutcome.Reset;
        await Assert.That(resetWon ^ (replacement == LocalCredentialReplacementOutcome.Replaced)).IsTrue();
        Snapshot committed = await fixture.ReadAsync();
        if (resetWon)
        {
            await Assert.That(replacement is LocalCredentialReplacementOutcome.InvalidChallenge or LocalCredentialReplacementOutcome.Conflict).IsTrue();
            await Assert.That(committed.Metadata.OperationId).IsEqualTo(request.OperationId);
            await Assert.That(committed.Metadata.State).IsEqualTo(LocalCredentialState.ChangeRequired);
            await Assert.That(await fixture.PasswordIsValidAsync(reset.TemporaryPassword!)).IsTrue();
            await Assert.That(await fixture.PasswordIsValidAsync(privatePassword)).IsFalse();
        }
        else
        {
            await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Conflict);
            await Assert.That(string.IsNullOrEmpty(reset.TemporaryPassword)).IsTrue();
            await Assert.That(committed.Metadata.OperationId).IsEqualTo(before.Metadata.OperationId);
            await Assert.That(committed.Metadata.State).IsEqualTo(LocalCredentialState.Ready);
            await Assert.That(await fixture.PasswordIsValidAsync(privatePassword)).IsTrue();
        }
        await Assert.That(await fixture.ReplaceAsync(authority: authority, password: NewPassword()))
            .IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(committed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DistinctResetOperationsHaveOnlyOnePlaintextWinnerForTheSamePredecessor(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialResetRequest firstRequest = fixture.Request(before);
        LocalCredentialResetRequest secondRequest = fixture.Request(before);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LocalCredentialResetResult> Run(LocalCredentialResetRequest command, TaskCompletionSource ready) => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            ready.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await fixture.StateStore(scope).ResetAsync(command, fixture.CancellationToken);
        }, fixture.CancellationToken);
        Task<LocalCredentialResetResult> first = Run(firstRequest, firstReady);
        Task<LocalCredentialResetResult> second = Run(secondRequest, secondReady);
        await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(fixture.CancellationToken);
        start.SetResult();

        LocalCredentialResetResult[] results = await Task.WhenAll(first, second).WaitAsync(fixture.CancellationToken);

        await Assert.That(results.Count(result => result.Outcome == LocalCredentialResetOutcome.Reset)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Outcome == LocalCredentialResetOutcome.Conflict)).IsEqualTo(1);
        await Assert.That(results.Count(result => !string.IsNullOrEmpty(result.TemporaryPassword))).IsEqualTo(1);
        LocalCredentialResetResult winner = results.Single(result => result.Outcome == LocalCredentialResetOutcome.Reset);
        Snapshot after = await fixture.ReadAsync();
        await Assert.That(after.Metadata.OperationId).IsEqualTo(winner.Receipt!.Operation.OperationId);
        await Assert.That(after.Operations.Count).IsEqualTo(before.Operations.Count + 1);
        await Assert.That(await fixture.PasswordIsValidAsync(winner.TemporaryPassword!)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task InvalidResetLedgerShapesAreRejectedByTheSelectedDatabase(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialResetResult reset = await fixture.ResetAsync(fixture.Request(await fixture.ReadAsync()));
        await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        Snapshot before = await fixture.ReadAsync();
        foreach (InvalidLedgerShape invalid in Enum.GetValues<InvalidLedgerShape>())
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            DbContext identity = fixture.Identity(scope);
            LocalIdentityCredentialOperation operation = await identity.Set<LocalIdentityCredentialOperation>()
                .SingleAsync(row => row.Id == before.Metadata.OperationId, fixture.CancellationToken);
            var entry = identity.Entry(operation);
            switch (invalid)
            {
                case InvalidLedgerShape.MissingPredecessor:
                    entry.Property(row => row.PreviousOperationId).CurrentValue = null;
                    break;
                case InvalidLedgerShape.MissingPredecessorStamp:
                    entry.Property(row => row.PreviousOperationConcurrencyStamp).CurrentValue = null;
                    break;
                case InvalidLedgerShape.MissingReason:
                    entry.Property(row => row.ResetReason).CurrentValue = null;
                    break;
                case InvalidLedgerShape.CreateWithResetMetadata:
                    entry.Property(row => row.Kind).CurrentValue = LocalCredentialOperationKind.Create;
                    entry.Property(row => row.VerifiedAt).CurrentValue = operation.CreatedAt;
                    break;
                case InvalidLedgerShape.SelfPredecessor:
                    entry.Property(row => row.PreviousOperationId).CurrentValue = operation.Id;
                    break;
                case InvalidLedgerShape.PendingReset:
                    entry.Property(row => row.Stage).CurrentValue = LocalCredentialOperationStage.ProvisioningPending;
                    break;
                default: throw new InvalidOperationException("Unsupported ledger corruption case.");
            }
            await Assert.ThrowsAsync<DbUpdateException>(() => identity.SaveChangesAsync(fixture.CancellationToken));
            await fixture.AssertUnchangedAsync(before);
        }
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, CallerWork.PendingChanges)]
    [Arguments(IdentityDatabaseTopology.Colocated, CallerWork.ActiveTransaction)]
    [Arguments(IdentityDatabaseTopology.External, CallerWork.PendingChanges)]
    [Arguments(IdentityDatabaseTopology.External, CallerWork.ActiveTransaction)]
    public async Task CallerOwnedWorkIsNeitherSavedDetachedNorCommittedByRejectedReset(
        IdentityDatabaseTopology topology, CallerWork work)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(scope);
        await using var transaction = work == CallerWork.ActiveTransaction
            ? await identity.Database.BeginTransactionAsync(fixture.CancellationToken) : null;
        var role = new LocalIdentityRole($"caller-{Guid.CreateVersion7():N}");
        identity.Set<LocalIdentityRole>().Add(role);
        if (transaction is not null) await identity.SaveChangesAsync(fixture.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.StateStore(scope)
            .ResetAsync(fixture.Request(before), fixture.CancellationToken));

        await Assert.That(identity.Entry(role).State).IsEqualTo(transaction is null ? EntityState.Added : EntityState.Unchanged);
        await Assert.That(ReferenceEquals(identity.Database.CurrentTransaction, transaction)).IsTrue();
        await using (AsyncServiceScope observer = fixture.Provider.CreateAsyncScope())
            await Assert.That(await fixture.Identity(observer).Set<LocalIdentityRole>()
                .AnyAsync(row => row.Id == role.Id, fixture.CancellationToken)).IsFalse();
        if (transaction is null) await identity.SaveChangesAsync(fixture.CancellationToken);
        else await transaction.CommitAsync(fixture.CancellationToken);
        await using (AsyncServiceScope observer = fixture.Provider.CreateAsyncScope())
            await Assert.That(await fixture.Identity(observer).Set<LocalIdentityRole>()
                .AnyAsync(row => row.Id == role.Id, fixture.CancellationToken)).IsTrue();
        await fixture.AssertUnchangedAsync(before);
    }

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    private sealed record OperationSnapshot(Guid Id, LocalCredentialOperationKind Kind, LocalCredentialOperationStage Stage,
        Guid InitiatorId, Guid PersonalActorId, Guid ExternalLoginId, Guid VerifiedBy, DateTime VerifiedAt,
        DateTime CreatedAt, DateTime? UpdatedAt, Guid Stamp, Guid? PreviousId, Guid? PreviousStamp, string? Reason)
    {
        public override string ToString() => nameof(OperationSnapshot);
    }

    private sealed record Snapshot(string? PasswordHash, string? SecurityStamp, string? ConcurrencyStamp,
        DateTime? UserUpdatedAt, bool EmailConfirmed, bool LockoutEnabled, DateTimeOffset? LockoutEnd, int AccessFailedCount,
        string TokenValue, LocalCredentialStateMetadata Metadata, IReadOnlyList<OperationSnapshot> Operations)
    {
        internal OperationSnapshot CurrentOperation => Operations.Single(operation => operation.Id == Metadata.OperationId);
        public override string ToString() => nameof(Snapshot);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"reset-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"reset-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private ServiceProvider? _provider;
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider => _provider!;
        internal CancellationToken CancellationToken => _timeout.Token;
        internal Guid CreatorId { get; } = Guid.CreateVersion7();
        internal Guid ResetActorId { get; } = Guid.CreateVersion7();
        internal ControlledClock Clock { get; } = new();
        internal ResetWriteFault WriteFault { get; } = new();
        internal LostCommitAcknowledgement CommitFault { get; } = new();
        internal LocalCredentialOperationReceipt OriginalReceipt { get; private set; } = null!;
        internal string CurrentPassword { get; private set; } = string.Empty;

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology,
            LocalCredentialState initialState = LocalCredentialState.ChangeRequired)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync().WaitAsync(TestContext.Current!.Execution.CancellationToken);
            var fixture = new Fixture { _topology = topology };
            fixture._timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await fixture.InitializeAsync(initialState); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>()
            : scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal LocalIdentityCredentialStateStore StateStore(AsyncServiceScope scope) => new(
            identityDbContext: Identity(scope), applicationDbContext: scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
            userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), timeProvider: Clock);
        internal LocalCredentialResetRequest Request(Snapshot current) => new(
            operationId: Guid.CreateVersion7(), initiatingApplicationUserId: ResetActorId,
            localSubjectId: OriginalReceipt.LocalSubjectId, expectedCurrentOperationId: current.Metadata.OperationId,
            expectedCurrentOperationConcurrencyStamp: current.CurrentOperation.Stamp,
            reason: $"  Supervised recovery {Guid.CreateVersion7():N}  ");
        internal LocalCredentialReplacementAuthority Authority(Snapshot current) => new(
            subject: new LocalCredentialReplacementSubject(localSubjectId: OriginalReceipt.LocalSubjectId,
                operationId: current.Metadata.OperationId, securityStamp: current.SecurityStamp!),
            issuedAtUtc: Clock.GetUtcNow(), expiresAtUtc: Clock.GetUtcNow().AddMinutes(5));
        internal async Task<LocalCredentialResetResult> ResetAsync(LocalCredentialResetRequest request)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await StateStore(scope).ResetAsync(request, CancellationToken);
        }
        internal async Task<LocalCredentialReplacementOutcome> ReplaceAsync(LocalCredentialReplacementAuthority authority, string password)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await StateStore(scope).ReplaceAsync(new LocalCredentialReplacementRequest(authority: authority, newPassword: password), CancellationToken);
        }
        internal async Task<bool> PasswordIsValidAsync(string password)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(OriginalReceipt.LocalSubjectId.ToString("D")))!;
            return await manager.CheckPasswordAsync(user, password).WaitAsync(CancellationToken);
        }
        internal async Task RestrictNativeAccountAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(OriginalReceipt.LocalSubjectId.ToString("D")))!;
            user.EmailConfirmed = false;
            await Assert.That((await manager.UpdateAsync(user)).Succeeded).IsTrue();
            await Assert.That((await manager.SetLockoutEnabledAsync(user, true)).Succeeded).IsTrue();
            await Assert.That((await manager.SetLockoutEndDateAsync(user, Clock.GetUtcNow().AddHours(1))).Succeeded).IsTrue();
            await Assert.That((await manager.AccessFailedAsync(user)).Succeeded).IsTrue();
        }
        internal async Task<Snapshot> ReadAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            DbContext identity = Identity(scope);
            LocalIdentityUser user = await identity.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == OriginalReceipt.LocalSubjectId, CancellationToken);
            string token = (await identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(row => row.UserId == user.Id && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && row.Name == LocalCredentialStateMetadata.TokenName).Select(row => row.Value).SingleAsync(CancellationToken))!;
            List<LocalIdentityCredentialOperation> operations = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .Where(row => row.LocalSubjectId == user.Id).OrderBy(row => row.Id).ToListAsync(CancellationToken);
            return new Snapshot(PasswordHash: user.PasswordHash, SecurityStamp: user.SecurityStamp, ConcurrencyStamp: user.ConcurrencyStamp,
                UserUpdatedAt: user.UpdatedAt, EmailConfirmed: user.EmailConfirmed, LockoutEnabled: user.LockoutEnabled,
                LockoutEnd: user.LockoutEnd, AccessFailedCount: user.AccessFailedCount,
                TokenValue: token, Metadata: JsonSerializer.Deserialize<LocalCredentialStateMetadata>(token)!,
                Operations: operations.Select(operation => new OperationSnapshot(Id: operation.Id, Kind: operation.Kind, Stage: operation.Stage,
                    InitiatorId: operation.InitiatingApplicationUserId, PersonalActorId: operation.PersonalActorId, ExternalLoginId: operation.ExternalLoginId,
                    VerifiedBy: operation.VerifiedByApplicationUserId, VerifiedAt: operation.VerifiedAt, CreatedAt: operation.CreatedAt,
                    UpdatedAt: operation.UpdatedAt, Stamp: operation.ConcurrencyStamp, PreviousId: operation.PreviousOperationId,
                    PreviousStamp: operation.PreviousOperationConcurrencyStamp, Reason: operation.ResetReason)).ToArray());
        }
        internal async Task AssertUnchangedAsync(Snapshot before)
        {
            Snapshot after = await ReadAsync();
            await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.TokenValue, before.TokenValue, StringComparison.Ordinal)).IsTrue();
            await Assert.That(after.UserUpdatedAt).IsEqualTo(before.UserUpdatedAt);
            await Assert.That(after.EmailConfirmed).IsEqualTo(before.EmailConfirmed);
            await Assert.That(after.LockoutEnabled).IsEqualTo(before.LockoutEnabled);
            await Assert.That(after.LockoutEnd).IsEqualTo(before.LockoutEnd);
            await Assert.That(after.AccessFailedCount).IsEqualTo(before.AccessFailedCount);
            await Assert.That(after.Operations.SequenceEqual(before.Operations)).IsTrue();
        }
        internal async Task AssertExactGraphAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await application.Actors.CountAsync(actor => actor.UserId == OriginalReceipt.LocalSubjectId
                && actor.Id == OriginalReceipt.PersonalActorId && !actor.IsDeleted && !actor.IsSuspended, CancellationToken)).IsEqualTo(1);
            await Assert.That(await application.UserExternalLogins.CountAsync(login => login.Id == OriginalReceipt.ExternalLoginId
                && login.UserId == OriginalReceipt.LocalSubjectId && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && login.ProviderKey == OriginalReceipt.LocalSubjectId.ToString("D"), CancellationToken)).IsEqualTo(1);
            await Assert.That(await Identity(scope).Set<LocalIdentityUser>().CountAsync(CancellationToken)).IsEqualTo(1);
            await Assert.That(await application.LocalIdentityUsers.CountAsync(CancellationToken))
                .IsEqualTo(_topology == IdentityDatabaseTopology.Colocated ? 1 : 0);
        }
        private async Task InitializeAsync(LocalCredentialState initialState)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath));
            IdentityBuilder identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            _provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                _topology == IdentityDatabaseTopology.External ? _identityPath : null, seedLookups: true, CancellationToken);
            await using (AsyncServiceScope seed = Provider.CreateAsyncScope())
            {
                var application = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
                foreach (Guid userId in new[] { CreatorId, ResetActorId })
                    application.Users.Add(new User
                    {
                        Id = userId,
                        EmailVerified = true,
                        CreatedAt = Clock.GetUtcNow().UtcDateTime,
                        Pii = new UserPii { Email = $"operator-{userId:N}@example.test", FirstName = "Instance", LastName = "Operator" }
                    });
                await application.SaveChangesAsync(CancellationToken);
            }
            string email = $"reset-{Guid.CreateVersion7():N}@example.test";
            await using (AsyncServiceScope create = Provider.CreateAsyncScope())
            {
                LocalCredentialCreateResult result = await StateStore(create).CreatePendingAsync(new LocalCredentialCreateRequest(
                    operationId: Guid.CreateVersion7(), initiatingApplicationUserId: CreatorId,
                    email: email, firstName: "Credential", lastName: "Owner"), CancellationToken);
                await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
                OriginalReceipt = result.Receipt!;
                CurrentPassword = result.TemporaryPassword!;
            }
            await using (AsyncServiceScope bind = Provider.CreateAsyncScope())
            {
                var application = bind.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var user = new User
                {
                    Id = OriginalReceipt.LocalSubjectId,
                    EmailVerified = true,
                    CreatedAt = Clock.GetUtcNow().UtcDateTime,
                    Pii = new UserPii { Email = email, FirstName = "Credential", LastName = "Owner" }
                };
                application.Actors.Add(new Actor
                {
                    Id = OriginalReceipt.PersonalActorId,
                    UserId = user.Id,
                    User = user,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Credential Owner" },
                    CreatedAt = Clock.GetUtcNow().UtcDateTime
                });
                application.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = OriginalReceipt.ExternalLoginId,
                    UserId = user.Id,
                    User = user,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                    AuthenticationProvider = null!,
                    ProviderKey = user.Id.ToString("D"),
                    CreatedAt = Clock.GetUtcNow().UtcDateTime
                });
                await application.SaveChangesAsync(CancellationToken);
            }
            await using (AsyncServiceScope activate = Provider.CreateAsyncScope())
            {
                LocalCredentialProvisioningSnapshot pending = (await StateStore(activate).ReadProvisioningAsync(OriginalReceipt.OperationId, CancellationToken))!;
                await Assert.That(await StateStore(activate).ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                    operationId: OriginalReceipt.OperationId, expectedOperationConcurrencyStamp: pending.OperationConcurrencyStamp), CancellationToken))
                    .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            }
            if (initialState == LocalCredentialState.Ready)
            {
                CurrentPassword = NewPassword();
                await Assert.That(await ReplaceAsync(authority: Authority(await ReadAsync()), password: CurrentPassword))
                    .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            }
            Clock.Advance(TimeSpan.FromMinutes(1));
        }
        private void Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, WriteFault, CommitFault);
        public async ValueTask DisposeAsync()
        {
            try { if (_provider is not null) await _provider.DisposeAsync(); }
            finally
            {
                _metadataCache.Dispose();
                foreach (string path in new[] { _applicationPath, _identityPath })
                {
                    File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
                }
                _timeout.Dispose();
            }
        }
    }

    private sealed class ControlledClock : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }
    private sealed class InjectedResetFailure : Exception;
    private sealed class ResetWriteFault : DbCommandInterceptor
    {
        private DbContext? _identity;
        private ResetWriteBoundary _boundary;
        internal bool Observed { get; private set; }
        internal int WritesObserved { get; private set; }
        internal int AffectedRows { get; private set; }
        internal void Arm(DbContext identity, ResetWriteBoundary boundary) { _identity = identity; _boundary = boundary; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _identity?.Database.CurrentTransaction is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase))
            {
                WritesObserved++;
                if (WritesObserved == (int)_boundary)
                {
                    _identity = null; Observed = true; AffectedRows = result;
                    throw new InjectedResetFailure();
                }
            }
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        private DbContext? _identity;
        internal bool Observed { get; private set; }
        internal void Arm(DbContext identity) => _identity = identity;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity))
            {
                _identity = null; Observed = true;
                throw new InjectedResetFailure();
            }
            return Task.CompletedTask;
        }
    }
}
