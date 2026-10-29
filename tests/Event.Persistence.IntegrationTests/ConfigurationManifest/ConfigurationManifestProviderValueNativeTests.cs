using System.Data;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestProviderValueNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task FailedHistoryWriter_RejectsAppliedProviderStatusAtItsOwnGuardWithoutMutation()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var original = await host.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        await original.Database.OpenConnectionAsync();
        var data = new ConfigurationManifestBootstrapData(original);
        Guid operationId = Guid.CreateVersion7();
        var scope = new JsonObject
        {
            ["Slugs"] = new JsonArray(JsonValue.Create("failed-history-provider-value")),
            ["Tenants"] = new JsonArray(),
            ["CreatedTenants"] = new JsonArray(),
            ["Revisions"] = new JsonArray(),
            ["Documents"] = new JsonArray(),
            ["Policies"] = new JsonArray(),
            ["Operations"] = new JsonArray(JsonValue.Create(operationId)),
            ["Operation"] = JsonValue.Create(operationId),
            ["Effect"] = JsonValue.Create(Guid.CreateVersion7())
        };
        string migration = fixture.NativeQueueEnrollment.MigratorLogin;
        string global = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!;
        DateTime occurred = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var operation = ConfigurationManifestOperation.Create(operationId, ConfigurationManifestAuditMode.Bootstrap,
            "v1alpha2", "ConfigurationManifest", "applied-is-not-failure-evidence",
            new string('a', ConfigurationManifestOperation.DigestLength), ConfigurationManifestOperationStatus.Applied,
            0, 0, 0, 0, null, null, occurred, occurred,
            new string('b', ConfigurationManifestOperation.DigestLength), 1);

        JsonObject before;
        PostgresException? rejection;
        await using (var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            before = await PostgresConfigurationManifestBootstrapContract.ReadValidationAsync(
                original, data, scope, migration, global, CancellationToken.None);
            var after = before.DeepClone().AsObject();
            var row = data.Row(operation);
            after["Operations"]!.AsArray().Add(row.DeepClone());
            data.Order(after);
            var changes = new JsonArray(new JsonObject
            {
                ["Tag"] = "Operations",
                ["Before"] = null,
                ["After"] = row
            });
            rejection = await Assert.ThrowsAsync<PostgresException>(() => PostgresConfigurationManifestBootstrapContract.WriteEvidenceAsync(
                original, data, scope, migration, global, before, changes, after, CancellationToken.None));
            await transaction.RollbackAsync();
        }

        await using (var verification = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var observed = await PostgresConfigurationManifestBootstrapContract.ReadValidationAsync(
                original, data, scope, migration, global, CancellationToken.None);
            await Assert.That(ConfigurationManifestBootstrapData.Same(before, observed)).IsTrue();
            await verification.RollbackAsync();
        }
        await Assert.That((rejection!.SqlState, rejection.MessageText))
            .IsEqualTo((PostgresErrorCodes.InsufficientPrivilege, "manifest_bootstrap_failed_history_only"));
    }
}
