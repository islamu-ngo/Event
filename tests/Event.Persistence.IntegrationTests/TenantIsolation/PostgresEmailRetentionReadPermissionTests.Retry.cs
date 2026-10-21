using System.Data;
using System.Diagnostics;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class PostgresEmailRetentionReadPermissionTests
{
    [Test]
    public async Task ConfiguredRetryCannotReanchorEmailRetentionToAReplacementBackend()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using (var migration = OpenMigrator())
            await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);

        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using (var configured = await factory.CreateDbContextAsync(token))
            await Assert.That(configured.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        var repository = new EmailRetentionTenantDiscoveryRepository(factory, fixture.NativeQueueEnrollment);
        DateTime cutoff = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var fault = new EmailRetentionRetryFault();
        var healthy = await repository.GetTenantIdsAsync(cutoff, 2, token);
        await Assert.That(healthy).IsEmpty();
        await Assert.That(fault.WarmReads).IsGreaterThanOrEqualTo(1);

        fault.Arm();
        IReadOnlyList<Guid>? escaped = null;
        Exception? failure = null;
        try
        {
            escaped = await repository.GetTenantIdsAsync(cutoff, 2, token);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        string observation = $"failure_type={failure?.GetType().FullName ?? "<none>"}; fault_count={fault.Faults}; escaped_page={escaped is not null}; replacement_observed={fault.ReplacementBackendObserved}; same_owner={fault.SameOwnerOnSubsequentReads}";
        await Assert.That(fault.Faults).IsEqualTo(1).Because(observation);
        await Assert.That(fault.OriginalBackend).IsGreaterThan(0);
        await Assert.That(fault.FaultWasTransient).IsTrue();
        await Assert.That(fault.ClosedOriginal).IsTrue();
        await Assert.That(fault.SameOwnerOnSubsequentReads).IsTrue().Because(observation);
        if (escaped is not null)
        {
            await Assert.That(fault.ReplacementBackendObserved).IsTrue().Because(observation);
            await Assert.That(escaped).IsEmpty();
        }
        await Assert.That(failure is UnauthorizedAccessException).IsTrue()
            .Because("email retention must not renew its original backend witness during a configured retry; " + observation);
        await Assert.That(escaped).IsNull();
    }

    private sealed class EmailRetentionRetryFault : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable listeners;
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        private ExploreDbContext? originalContext;
        private NpgsqlConnection? originalConnection;
        private DbContextId originalLease;
        private bool armed;
        public int WarmReads { get; private set; }
        public int Faults { get; private set; }
        public int OriginalBackend { get; private set; }
        public bool FaultWasTransient { get; private set; }
        public bool ClosedOriginal { get; private set; }
        public bool ReplacementBackendObserved { get; private set; }
        public bool SameOwnerOnSubsequentReads { get; private set; } = true;

        public EmailRetentionRetryFault() => listeners = DiagnosticListener.AllListeners.Subscribe(this);

        public void Arm()
        {
            if (originalContext is null || WarmReads == 0)
                throw new InvalidOperationException("A genuine successful email retention read must precede the fault.");
            armed = true;
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            if (diagnostic.Value is not CommandExecutedEventData command
                || command.ExecuteMethod != DbCommandMethod.ExecuteReader
                || command.Context is not ExploreDbContext context
                || command.Command.Connection is not NpgsqlConnection connection)
                return;
            if (!armed)
            {
                originalContext ??= context;
                if (ReferenceEquals(context, originalContext)) WarmReads++;
                return;
            }
            if (!ReferenceEquals(context, originalContext))
                return;
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || connection.State != ConnectionState.Open
                || !ReferenceEquals(context.Database.GetDbConnection(), connection)
                || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The fault must target the actual sealed-null Global email retention owner.");
            if (Faults != 0)
            {
                SameOwnerOnSubsequentReads &= context.ContextId == originalLease
                    && ReferenceEquals(connection, originalConnection);
                ReplacementBackendObserved |= connection.ProcessID != OriginalBackend;
                return;
            }
            originalLease = context.ContextId;
            originalConnection = connection;
            OriginalBackend = connection.ProcessID;
            Faults++;
            NpgsqlConnection.ClearPool(connection);
            connection.Close();
            ClosedOriginal = connection.State == ConnectionState.Closed;
            var transient = new NpgsqlException("Controlled email retention connection interruption.",
                new System.IO.IOException("The test closed the original connector after its actual email retention query."));
            FaultWasTransient = transient.IsTransient;
            throw transient;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void Dispose()
        {
            listeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
