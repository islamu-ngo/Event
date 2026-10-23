using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class ManagedTenantWorkerCreationNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplayingTheSameCurrentDeliveryAcknowledgesOnlyItsAlreadyCommittedOperation(bool refused)
    {
        await using var scenario = await CreateWorkflowAsync(new WorkerWorkflowProbe("none"));
        var scheduled = await ScheduleOriginalAsync(scenario);
        if (refused)
        {
            await using var revoke = OpenMigrator();
            var registration = await revoke.ManagedControlPlaneRegistrations.SingleAsync(row =>
                row.ManagedInstanceId == scenario.ManagedInstanceId, scenario.Token);
            registration.Revoke(DateTime.UtcNow);
            await revoke.SaveChangesAsync(scenario.Token);
        }
        var claim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);
        await Assert.That(await scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token)).IsTrue();
        await using var before = OpenMigrator();
        var completed = await before.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(completed.Status).IsEqualTo(refused ? ManagedTenantProvisioningStatus.Failed : ManagedTenantProvisioningStatus.Succeeded);
        string originalImage = System.Text.Json.JsonSerializer.Serialize(completed);
        Guid[] users = await before.Users.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        Guid[] actors = await before.Actors.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);

        // Outbox completion may be lost after the creator's physical commit.
        // Re-enter through another genuine execution scope, never a copied issuer.
        await Assert.That(await scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token)).IsTrue();

        await using var observer = OpenMigrator();
        var unchanged = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(unchanged)).IsEqualTo(originalImage);
        await Assert.That(await observer.Tenants.CountAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token)).IsEqualTo(refused ? 0 : 1);
        Guid[] actualUsers = await observer.Users.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        Guid[] actualActors = await observer.Actors.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(scenario.Token);
        await Assert.That(users.SequenceEqual(actualUsers)).IsTrue();
        await Assert.That(actors.SequenceEqual(actualActors)).IsTrue();
    }

    [Test]
    public async Task OriginalManagedCancellationSettlesOnlyItsPendingGenerationWithoutCreatingATenant()
    {
        await using var scenario = await CreateWorkflowAsync(new WorkerWorkflowProbe("none"));
        var scheduled = await ScheduleOriginalAsync(scenario);

        var foreign = await scenario.Host.CancelManagedWorkerAsync(Guid.CreateVersion7(), scheduled.Id, scenario.Token);
        await Assert.That(foreign.IsSuccess).IsFalse();
        await using (var unchanged = OpenMigrator())
            await Assert.That((await unchanged.ManagedTenantProvisioningOperations.AsNoTracking()
                .SingleAsync(row => row.Id == scheduled.Id, scenario.Token)).Status)
                .IsEqualTo(ManagedTenantProvisioningStatus.Pending);

        var cancelled = await scenario.Host.CancelManagedWorkerAsync(scenario.ManagedInstanceId, scheduled.Id, scenario.Token);

        await Assert.That(cancelled.IsSuccess).IsTrue();
        await using var observer = OpenMigrator();
        var completed = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(completed.Status).IsEqualTo(ManagedTenantProvisioningStatus.Cancelled);
        await Assert.That(completed.CurrentOutboxMessageId).IsEqualTo(scheduled.CurrentOutboxMessageId);
        await Assert.That(completed.RequestJson).IsNull();
        await Assert.That(completed.CancelledAt).IsNotNull();
        await Assert.That(completed.TenantId).IsNull();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token)).IsFalse();
    }

    [Test]
    public async Task ActualExhaustedDeliveryReconcilesOnlyItsCurrentOperationOnTheDedicatedOwner()
    {
        await using var scenario = await CreateWorkflowAsync(new WorkerWorkflowProbe("none"));
        var scheduled = await ScheduleOriginalAsync(scenario);
        GenericOutboxClaim? lastClaim = null;
        OutboxFailureTransition transition = OutboxFailureTransition.NotOwned;
        for (int attempt = 0; attempt < 10 && transition != OutboxFailureTransition.DeadLettered; attempt++)
        {
            lastClaim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);
            transition = await scenario.Host.FailManagedWorkerDeliveryAsync(lastClaim, scenario.Token);
            await Assert.That(transition is OutboxFailureTransition.RetryScheduled or OutboxFailureTransition.DeadLettered).IsTrue();
        }
        await Assert.That(transition).IsEqualTo(OutboxFailureTransition.DeadLettered);
        var deadLetter = await scenario.Host.ReadManagedWorkerDeadLetterAsync(lastClaim!, scenario.Token);
        await Assert.That(deadLetter.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(deadLetter.MessageId).IsEqualTo(scheduled.CurrentOutboxMessageId);

        await Assert.That(await scenario.Host.ReconcileManagedWorkerAsync(deadLetter, scenario.Token)).IsTrue();

        await using var observer = OpenMigrator();
        var failed = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
            .SingleAsync(row => row.Id == scheduled.Id, scenario.Token);
        await Assert.That(failed.Status).IsEqualTo(ManagedTenantProvisioningStatus.Failed);
        await Assert.That(failed.CurrentOutboxMessageId).IsEqualTo(deadLetter.MessageId);
        await Assert.That(failed.FailureCode).IsEqualTo(ReconcileManagedTenantProvisioningDeadLetterCommandHandler.FailureCode);
        await Assert.That(failed.RequestJson).IsNull();
        await Assert.That(failed.TenantId).IsNull();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Slug == scenario.Request.TenantSlug, scenario.Token)).IsFalse();
    }
}
