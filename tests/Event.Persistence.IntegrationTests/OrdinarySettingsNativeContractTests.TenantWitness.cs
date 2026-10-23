using System.Data;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class OrdinarySettingsNativeContractTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyCatalogIsAcceptedButSnapshotBeforeFirstTenantIsRejected(bool insertFirstTenant)
    {
        await fixture.ResetAsync();
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await Assert.That(await migration.Tenants.CountAsync()).IsEqualTo(0);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance, key: DisclosureKey, value: "\"MultiTenant\"");
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject after = SystemAfter(before, request, DisclosureKey);
        JsonNode expected = before["Image"]!.DeepClone();
        expected["System"]![DisclosureKey] = after.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            // Establish the actual empty SERIALIZABLE snapshot before the other
            // connector commits its first membership. No timing or lock polling.
            await ReadAsync(caller, keys: request["Keys"]);
            if (insertFirstTenant)
            {
                migration.Tenants.Add(Tenant(Guid.CreateVersion7()));
                await migration.SaveChangesAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(() => AcquireAsync(caller, before, request));
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
                await transaction.RollbackAsync();
            }
            else
            {
                await AcquireAsync(caller, before, request);
                await WriteAsync(caller, "System", 0, before["Image"]!["System"]![DisclosureKey], after, DisclosureKey);
                await CheckAsync(caller, "complete", expected);
                await transaction.CommitAsync();
            }
        }
        await Assert.That(JsonNode.DeepEquals(insertFirstTenant ? before["Image"] : expected,
            (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
        await Assert.That(await migration.Set<EventDiscoveryRevision>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task MembershipWitnessChangesForMembershipButNotTenantFencesOrMetadata()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        long revision = (await ReadAsync(caller))["TenantRevision"]!.GetValue<long>();
        Guid id = Guid.CreateVersion7(), replacement = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.Add(Tenant(id));
        await migration.SaveChangesAsync();
        await Assert.That((await ReadAsync(caller))["TenantRevision"]!.GetValue<long>()).IsEqualTo(revision + 1);
        var sql = new HumanPrincipalSourceSql(migration);
        string table = sql.Table(typeof(Tenant)), key = sql.Column(typeof(Tenant), nameof(Explore.Domain.Tenant.Id));
        await migration.Database.ExecuteSqlRawAsync($"UPDATE {table} SET {key} = {key} WHERE {key} = {{0}}", id);
        await migration.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET {sql.Column(typeof(Tenant), nameof(Explore.Domain.Tenant.FullName))} = {{0}} WHERE {key} = {{1}}",
            "Changed metadata", id);
        await Assert.That((await ReadAsync(caller))["TenantRevision"]!.GetValue<long>()).IsEqualTo(revision + 1);
        await migration.Database.ExecuteSqlRawAsync($"UPDATE {table} SET {key} = {{0}} WHERE {key} = {{1}}", replacement, id);
        await Assert.That((await ReadAsync(caller))["TenantRevision"]!.GetValue<long>()).IsEqualTo(revision + 2);
        await migration.Database.ExecuteSqlRawAsync($"DELETE FROM {table} WHERE {key} = {{0}}", replacement);
        await Assert.That((await ReadAsync(caller))["TenantRevision"]!.GetValue<long>()).IsEqualTo(revision + 3);
    }

    [Test]
    public async Task DisclosureFanoutOverflowRollsBackTheSetting()
    {
        await fixture.ResetAsync();
        await InstallAsync();
        Guid templateId = Guid.CreateVersion7();
        string slugPrefix = $"ordinary-overflow-{templateId:N}-";
        try
        {
            await using var migration = fixture.CreateDbContext();
            migration.Tenants.Add(Tenant(templateId));
            await migration.SaveChangesAsync();
            await migration.Database.OpenConnectionAsync();
            await using (var setup = migration.Database.GetDbConnection().CreateCommand())
            {
                // This setup runs every real membership/source trigger for 1,024 rows.
                // Keep its budget separate from the native overflow operation below.
                setup.CommandTimeout = 300;
                var sql = new HumanPrincipalSourceSql(migration);
                string table = sql.Table(typeof(Tenant));
                string id = sql.Column(typeof(Tenant), nameof(Explore.Domain.Tenant.Id));
                setup.CommandText = $"""
                    WITH ids AS MATERIALIZED (
                        SELECT pg_catalog.uuidv7() AS id
                        FROM pg_catalog.generate_series(1,{EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants})
                    )
                    INSERT INTO {table}
                    SELECT (pg_catalog.jsonb_populate_record(NULL::{table},
                        pg_catalog.to_jsonb(template) || pg_catalog.jsonb_build_object(
                            'id',ids.id,'slug',@prefix || ids.id::text))).*
                    FROM {table} template CROSS JOIN ids WHERE template.{id} = @template
                    """;
                var parameter = setup.CreateParameter();
                parameter.ParameterName = "template";
                parameter.Value = templateId;
                setup.Parameters.Add(parameter);
                var prefix = setup.CreateParameter();
                prefix.ParameterName = "prefix";
                prefix.Value = slugPrefix;
                setup.Parameters.Add(prefix);
                await Assert.That(await setup.ExecuteNonQueryAsync())
                    .IsEqualTo(EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants);
            }
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            JsonObject request = Request(SettingScope.Instance, key: DisclosureKey, value: "\"MultiTenant\"");
            JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
            await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await AcquireAsync(caller, before, request);
                var error = await Assert.ThrowsAsync<PostgresException>(() => WriteAsync(caller, "System", 0,
                    before["Image"]!["System"]![DisclosureKey], SystemAfter(before, request, DisclosureKey), DisclosureKey));
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.ProgramLimitExceeded);
                await transaction.RollbackAsync();
            }
            await Assert.That(JsonNode.DeepEquals(before["Image"], (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
            await Assert.That(await migration.Set<EventDiscoveryRevision>().CountAsync()).IsEqualTo(0);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Database.OpenConnectionAsync();
            await using (var command = cleanup.Database.GetDbConnection().CreateCommand())
            {
                var sql = new HumanPrincipalSourceSql(cleanup);
                command.CommandTimeout = 300;
                command.CommandText = $"""
                    DELETE FROM {sql.Table(typeof(Tenant))}
                    WHERE {sql.Column(typeof(Tenant), nameof(Explore.Domain.Tenant.Id))} = @template
                        OR {sql.Column(typeof(Tenant), nameof(Explore.Domain.Tenant.Slug))} LIKE @prefix
                    """;
                foreach (var (name, value) in new (string, object)[]
                {
                    ("template", templateId), ("prefix", slugPrefix + "%")
                })
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value;
                    command.Parameters.Add(parameter);
                }
                await command.ExecuteNonQueryAsync();
            }
            await fixture.ResetAsync();
        }
    }

    [Test]
    public async Task DisclosureEpochOverflowRollsBackEveryTenantAndSetting()
    {
        await fixture.ResetAsync();
        await InstallAsync();
        Guid first = Guid.Parse("10000000-0000-7000-8000-000000000001");
        Guid last = Guid.Parse("f0000000-0000-7000-8000-000000000001");
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(first), Tenant(last));
        await migration.SaveChangesAsync();
        migration.Set<EventDiscoveryRevision>().AddRange(
            new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = first, DisclosureEpoch = 9, IdentityEpoch = 11 },
            new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = last, DisclosureEpoch = long.MaxValue, IdentityEpoch = 17 });
        await migration.SaveChangesAsync();
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            JsonObject request = Request(SettingScope.Instance, key: DisclosureKey, value: "\"MultiTenant\"");
            JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
            JsonObject after = SystemAfter(before, request, DisclosureKey);
            JsonNode expected = before["Image"]!.DeepClone();
            expected["System"]![DisclosureKey] = after.DeepClone();
            await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await AcquireAsync(caller, before, request);
                await WriteAsync(caller, "System", 0, before["Image"]!["System"]![DisclosureKey], after, DisclosureKey);
                await CheckAsync(caller, "complete", expected);
                var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.NumericValueOutOfRange);
            }
            await Assert.That(JsonNode.DeepEquals(before["Image"], (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
            var epochs = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
                .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
            await Assert.That(epochs[first]).IsEqualTo((9L, 11L));
            await Assert.That(epochs[last]).IsEqualTo((long.MaxValue, 17L));
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }
}
