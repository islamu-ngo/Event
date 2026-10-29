using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalBootstrapConvergenceTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReceiptSourceRetainsGlobalOwnerAndBlocksTheActualCredentialActivation(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        await using var originalScope = fixture.Provider.CreateAsyncScope();
        var original = fixture.Application(originalScope);
        original.BindTenantLease();
        var expected = await CreatePendingReceiptAsync(fixture, originalScope);
        await using var source = new ConfiguredLocalBootstrapReceiptSource(original,
            () => ReceiptWriters(fixture, originalScope, topology));
        var lease = await source.AcquireAsync(expected, fixture.Token);
        await using var competingScope = fixture.Provider.CreateAsyncScope();
        fixture.Application(competingScope).BindTenantLease();
        Task<LocalCredentialActivationOutcome>? activation = null;
        try
        {
            await source.RequireCurrentAsync(lease, expected, fixture.Token);
            activation = fixture.Store(competingScope).ActivateChangeRequiredAsync(
                new(expected.Receipt.OperationId, expected.OperationConcurrencyStamp), fixture.Token);
            await Assert.That(activation.IsCompleted).IsFalse();
            await using var enrollment = new EventDiscoveryEnrollmentGate(original);
            await using (await enrollment.AcquireAsync(fixture.Token))
            {
                await using var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable, fixture.Token);
                await source.RequireCurrentAsync(lease, expected, fixture.Token);
                await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(activation.IsCompleted).IsFalse();
                await transaction.RollbackAsync(fixture.Token);
            }
            await Assert.That(original.Database.CurrentTransaction).IsNull();
            await Assert.That(activation.IsCompleted).IsFalse();
        }
        finally
        {
            await lease.DisposeAsync();
        }
        // No application user graph was enrolled, so the real activation may resume
        // but must not activate the still-unbound pending credential.
        await Assert.That(await activation!.WaitAsync(fixture.Token)).IsEqualTo(LocalCredentialActivationOutcome.BindingIncomplete);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            source.RequireCurrentAsync(lease, expected, fixture.Token));
        await Assert.That((await fixture.Store(originalScope).ReadProvisioningAsync(
            expected.Receipt.OperationId, fixture.Token))!.Receipt.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ReceiptTerminalDriftRejectsAndRollsBackTheOriginalGlobalPendingWrite(
        IdentityDatabaseTopology topology, bool profile)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        await using var originalScope = fixture.Provider.CreateAsyncScope();
        var original = fixture.Application(originalScope);
        original.BindTenantLease();
        var expected = await CreatePendingReceiptAsync(fixture, originalScope);
        await using var source = new ConfiguredLocalBootstrapReceiptSource(original,
            () => ReceiptWriters(fixture, originalScope, topology));
        await using var lease = await source.AcquireAsync(expected, fixture.Token);
        await source.RequireCurrentAsync(lease, expected, fixture.Token);
        await using (var privileged = fixture.Provider.CreateAsyncScope())
        {
            DbContext identity = topology == IdentityDatabaseTopology.External
                ? privileged.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : fixture.Application(privileged);
            if (profile)
                await identity.Set<LocalIdentityUser>().Where(row => row.Id == expected.Receipt.LocalSubjectId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FirstName, "Changed receipt profile"), fixture.Token);
            else
                await identity.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == expected.Receipt.OperationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()), fixture.Token);
        }
        await using var enrollment = new EventDiscoveryEnrollmentGate(original);
        await using (await enrollment.AcquireAsync(fixture.Token))
        {
            await using var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable, fixture.Token);
            original.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                expected.Receipt.OperationId, Explore.Domain.Enums.DeploymentMode.MultiTenant, DateTime.UtcNow));
            await original.SaveChangesAsync(fixture.Token);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                source.RequireCurrentAsync(lease, expected, fixture.Token));
            await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync(fixture.Token);
        }
        await using var observation = fixture.Provider.CreateAsyncScope();
        await Assert.That(await fixture.Application(observation).InstanceBootstrapStates
            .AnyAsync(row => row.Id == expected.Receipt.OperationId, fixture.Token)).IsFalse();
        await Assert.That(await fixture.Application(observation).PlatformUserRoles
            .AnyAsync(fixture.Token)).IsFalse();
    }

    [Test]
    public async Task ReceiptSourceRejectsAHandmadeLeaseWithoutAcquiringCredentialAuthority()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var original = fixture.Application(scope);
        original.BindTenantLease();
        var expected = await CreatePendingReceiptAsync(fixture, scope);
        await using var source = new ConfiguredLocalBootstrapReceiptSource(original,
            () => ReceiptWriters(fixture, scope, IdentityDatabaseTopology.External));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            source.RequireCurrentAsync(new HandmadeReceiptLease(), expected, fixture.Token));
        await Assert.That(original.Database.CurrentTransaction).IsNull();
        await Assert.That((await fixture.Store(scope).ReadProvisioningAsync(
            expected.Receipt.OperationId, fixture.Token))!.Receipt.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
    }

    private static async Task<LocalCredentialProvisioningSnapshot> CreatePendingReceiptAsync(Fixture fixture, AsyncServiceScope scope)
    {
        Guid operationId = Guid.CreateVersion7();
        var request = new LocalCredentialCreateRequest(operationId, operationId,
            email: null, firstName: "Receipt", lastName: "Owner", username: "receipt-" + operationId.ToString("N"));
        var store = fixture.Store(scope);
        var created = await store.CreateBootstrapPendingAsync(request, fixture.Subject, fixture.OriginalPassword, fixture.Token);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        return await store.ReadProvisioningAsync(operationId, fixture.Token)
            ?? throw new InvalidOperationException("The actual credential creation must publish its receipt.");
    }

    private static LocalIdentityWriterDbContextFactory ReceiptWriters(
        Fixture fixture, AsyncServiceScope scope, IdentityDatabaseTopology topology)
    {
        var application = fixture.Application(scope);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = new SqliteConnectionStringBuilder(application.Database.GetConnectionString()).DataSource
        };
        var external = topology == IdentityDatabaseTopology.External ? new ExternalIdentityDatabaseDescriptor(
            PrimaryDatabaseProvider.Sqlite,
            scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>().Database.GetConnectionString()!,
            IdentityDatabaseConfiguration.DefaultSchema, null, null) : null;
        return new(options, external, poolSize: 1);
    }

    private sealed class HandmadeReceiptLease : ILocalBootstrapReceiptLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
