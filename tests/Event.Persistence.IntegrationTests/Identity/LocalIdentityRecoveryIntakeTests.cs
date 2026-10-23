using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Persistence.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityRecoveryIntakeTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PrecommitFailureReturnsOnlyInternalCategoryAndRollsBackNativeUserWrite(IdentityDatabaseTopology topology)
    {
        var fault = new IntakeWriteFault();
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology, interceptor: fault);
        var before = await fixture.Native.ReadAsync();
        fault.Failure = () => new TransientStoreFailure();
        await using (var intake = fixture.Open())
        {
            AccountAuthorityLifecycleEmailResult result = await intake.Router.RequestPasswordResetAsync(
                Request(fixture), fixture.CancellationToken);
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
            await Assert.That(result.ReasonCode).IsEqualTo("local_intake_store_unavailable");
            await Assert.That(result.DelegationRecorded).IsFalse();
            await Assert.That(result.NotificationIntentId).IsNull();
            await Assert.That(result.LocalDelegationId).IsNull();
        }
        await Assert.That(fault.Triggered).IsTrue();
        await Assert.That(await fixture.Native.ReadAsync()).IsEqualTo(before);
        await using var read = fixture.Open();
        await Assert.That(await read.Deliveries.ReadPendingAsync(10, fixture.CancellationToken)).IsEmpty();
        await Assert.That(await read.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await read.Application.EmailDispatchOutbox.CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActualNativeOperationPrimaryKeyConflictDoesNotInventAnotherDurableRecovery(IdentityDatabaseTopology topology)
    {
        var fault = new IntakeWriteFault();
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology, interceptor: fault);
        LocalIdentityLifecyclePointer original;
        await using (var intake = fixture.Open())
        {
            await Assert.That((await intake.Router.RequestPasswordResetAsync(Request(fixture), fixture.CancellationToken)).DelegationRecorded).IsTrue();
            original = (await intake.Deliveries.ReadPendingAsync(10, fixture.CancellationToken)).Single();
        }
        fixture.Native.Clock.Advance(TimeSpan.FromMinutes(16));
        var before = await fixture.Native.ReadAsync();
        fault.DuplicateId = original.OperationId;
        await using (var intake = fixture.Open())
        {
            var result = await intake.Router.RequestPasswordResetAsync(Request(fixture), fixture.CancellationToken);
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
            await Assert.That(result.ReasonCode).IsEqualTo("local_intake_conflict");
            await Assert.That(result.DelegationRecorded).IsFalse();
        }
        await Assert.That(fault.Triggered).IsTrue();
        await Assert.That(await fixture.Native.ReadAsync()).IsEqualTo(before);
        await using var read = fixture.Open();
        await Assert.That(await read.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(1);
        await Assert.That(await read.Lifecycle.IssueTransportTokenAsync(original, fixture.CancellationToken)).IsNull();
        await Assert.That(await read.Deliveries.ReadPendingAsync(10, fixture.CancellationToken)).IsEmpty();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PermanentNativeConstraintFailureIsNotDisguisedAsAcceptedRecovery(IdentityDatabaseTopology topology)
    {
        var fault = new IntakeWriteFault();
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology, interceptor: fault);
        fault.Failure = () => new DbUpdateException("Controlled foreign-key failure.",
            new SqliteException("Controlled foreign-key failure.", 19, 787));
        await using var intake = fixture.Open();
        await Assert.That(async () => await intake.Router.RequestPasswordResetAsync(Request(fixture), fixture.CancellationToken))
            .Throws<DbUpdateException>();
        await Assert.That(fault.Triggered).IsTrue();
        await Assert.That(await intake.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NativeCancellationRemainsCancellationThroughRealRecoveryHandler(IdentityDatabaseTopology topology)
    {
        var fault = new IntakeWriteFault();
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology, interceptor: fault);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        fault.Failure = () =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        };
        await using var intake = fixture.Open();
        string identifier = (await intake.Identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Native.Receipt.LocalSubjectId, fixture.CancellationToken)).Email!;
        var handler = new RequestLocalPasswordRecoveryCommandHandler(intake.Lifecycle, intake.Router);
        await Assert.That(async () => await handler.ExecuteAsync(
            new RequestLocalPasswordRecoveryCommand(new LocalPasswordRecoveryRequestDto { Identifier = identifier }), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(fault.Triggered).IsTrue();
        await Assert.That(await intake.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
    }

    private static AccountAuthorityLifecycleEmailRequest Request(LocalLifecycleDeliveryFixture fixture) =>
        new(fixture.Native.Receipt.LocalSubjectId, fixture.Native.Receipt.ExternalLoginId);

    private sealed class IntakeWriteFault : SaveChangesInterceptor
    {
        internal Func<Exception>? Failure { get; set; }
        internal Guid? DuplicateId { get; set; }
        internal bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var operation = eventData.Context!.ChangeTracker.Entries<LocalIdentityLifecycleOperation>()
                .SingleOrDefault(entry => entry.State == EntityState.Added);
            if (operation is not null && (Failure is not null || DuplicateId is not null))
            {
                Triggered = true;
                if (Failure is not null) throw Failure();
                operation.Property(row => row.Id).CurrentValue = DuplicateId!.Value;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransientStoreFailure() : DbException("Controlled transient store failure.")
    {
        public override bool IsTransient => true;
    }
}
