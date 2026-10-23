using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresManifestReplayAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ReplayReadsBothSafeTenantResultsForOnlyTheSelectedOperation(bool foreignLease, bool globalExecution)
    {
        await fixture.ResetAsync();
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var seed = fixture.CreateDbContext();
        ConfigurationManifestOperation selected = Operation(1, 2, 1, 1);
        ConfigurationManifestOperation other = Operation(2, 1, 1, 0);
        var created = ConfigurationManifestTenantResult.Create(selected.Id, rows.A,
            ConfigurationManifestTenantResultStatus.Created, ["event_reporting.intake_enabled"], ["branding"], Now);
        var skipped = ConfigurationManifestTenantResult.Create(selected.Id, rows.B,
            ConfigurationManifestTenantResultStatus.SkippedExisting, [], [], Now);
        var unrelated = ConfigurationManifestTenantResult.Create(other.Id, rows.A,
            ConfigurationManifestTenantResultStatus.Created, ["events.require_approval"], [], Now);
        seed.AddRange(selected, other, created, skipped, unrelated);
        await seed.SaveChangesAsync();
        Guid lease = foreignLease ? rows.B : rows.A;
        await using var request = rows.Context(lease);
        await rows.AssertOrdinaryRoleAsync(request);
        string login = await request.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"")
            .SingleAsync();
        string Table<T>()
        {
            var entity = seed.Model.FindEntityType(typeof(T))!;
            return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }
        string tables = $"{Table<ConfigurationManifestOperation>()}, {Table<ConfigurationManifestTenantResult>()}";
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var permission = administration.CreateCommand();
        permission.CommandText = $"GRANT SELECT ON {tables} TO \"{login}\"";
        await permission.ExecuteNonQueryAsync();
        permission.CommandText = $"GRANT {PostgresManifestReplayContract.RuntimeRole} TO \"{login}\"";
        await permission.ExecuteNonQueryAsync();
        try
        {
            await Assert.That(await request.ConfigurationManifestTenantResults.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId != lease)).IsFalse();
            await using var authority =
                await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                    fixture, PostgresManifestReplayContract.GlobalRole);
            await using var global = await authority.Factory.CreateDbContextAsync();
            PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.ConfigurationManifestTenantResults.IgnoreQueryFilters().AnyAsync());
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
            var repository = new ConfigurationManifestOperationRepository(globalExecution ? global : request);

            IReadOnlyList<ConfigurationManifestTenantResult> result =
                await repository.GetResultsByOperationIdAsync(selected.Id, default);

            await Assert.That(result.Select(row => row.Id)).IsEquivalentTo([created.Id, skipped.Id]);
            await Assert.That(result.All(row => row.OperationId == selected.Id)).IsTrue();
            await Assert.That(result.Single(row => row.Id == created.Id).ChangedSettingKeyNames)
                .IsEquivalentTo(["event_reporting.intake_enabled"]);
            await Assert.That(result.Single(row => row.Id == skipped.Id).Status)
                .IsEqualTo(ConfigurationManifestTenantResultStatus.SkippedExisting);
            await Assert.That(result.Single(row => row.Id == skipped.Id).ChangedKeyNames).IsEmpty();
            await Assert.That(request.TenantFilterTenantId).IsEqualTo((Guid?)lease);
            await Assert.That(request.ChangeTracker.Entries()).IsEmpty();
            await Assert.That(await request.ConfigurationManifestTenantResults.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId != lease)).IsFalse();
        }
        finally
        {
            permission.CommandText = $"REVOKE SELECT ON {tables} FROM \"{login}\"";
            await permission.ExecuteNonQueryAsync();
            permission.CommandText = $"REVOKE {PostgresManifestReplayContract.RuntimeRole} FROM \"{login}\"";
            await permission.ExecuteNonQueryAsync();
        }
    }

    private static ConfigurationManifestOperation Operation(int generation, int requested, int created, int skipped) =>
        ConfigurationManifestOperation.Create(ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2", "ConfigurationManifest", "replay-authority",
            new string('a', 64), ConfigurationManifestOperationStatus.Applied,
            requested, created, skipped, 0, null, null, Now, Now,
            instanceSectionDigest: new string('b', 64), bootstrapGeneration: generation);
}
