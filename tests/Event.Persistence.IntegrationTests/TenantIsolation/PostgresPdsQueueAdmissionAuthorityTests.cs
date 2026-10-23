using Explore.Persistence.Database.ProviderPrimitives;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresPdsQueueAdmissionAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReapplyingInstalledPermissionsPreservesExactColumnsAndRefusesGrantDrift(bool grantOption)
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPdsQueueAdmissionContract.ApplyAsync(context);
        await PostgresPdsQueueAdmissionContract.ApplyAsync(context);

        var entity = context.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        var table = StoreObjectIdentifier.Table(
            entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(table.Name, table.Schema);
        string column = entity.FindProperty(grantOption
            ? nameof(PdsSyncOutbox.Id) : nameof(PdsSyncOutbox.LeaseOwner))!.GetColumnName(table)!;
        string identifier = names.DelimitIdentifier(column);
        string owner = names.DelimitIdentifier(PostgresPdsQueueAdmissionContract.OwnerRole);
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using (var grant = administrator.CreateCommand())
        {
            grant.CommandText = $"GRANT SELECT ({identifier}) ON {relation} TO {owner}"
                + (grantOption ? " WITH GRANT OPTION" : string.Empty);
            await grant.ExecuteNonQueryAsync();
        }
        try
        {
            bool rejected = false;
            try { await PostgresPdsQueueAdmissionContract.ApplyAsync(context); }
            catch (PostgresException exception) when (exception.SqlState == "P0001")
            {
                rejected = true;
            }
            await Assert.That(rejected).IsTrue();
            await using var observe = administrator.CreateCommand();
            observe.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute attribute
                    CROSS JOIN LATERAL pg_catalog.aclexplode(attribute.attacl) privilege
                    WHERE attribute.attrelid = @relation::regclass AND attribute.attname = @column
                      AND privilege.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @owner)
                      AND privilege.privilege_type = 'SELECT' AND privilege.is_grantable = @grantable)
                """;
            observe.Parameters.AddWithValue("relation", relation);
            observe.Parameters.AddWithValue("column", column);
            observe.Parameters.AddWithValue("owner", PostgresPdsQueueAdmissionContract.OwnerRole);
            observe.Parameters.AddWithValue("grantable", grantOption);
            await Assert.That(await observe.ExecuteScalarAsync()).IsEqualTo(true);
        }
        finally
        {
            await using var revoke = administrator.CreateCommand();
            revoke.CommandText = grantOption
                ? $"REVOKE GRANT OPTION FOR SELECT ({identifier}) ON {relation} FROM {owner}"
                : $"REVOKE SELECT ({identifier}) ON {relation} FROM {owner}";
            await revoke.ExecuteNonQueryAsync();
        }
        await PostgresPdsQueueAdmissionContract.ApplyAsync(context);
    }

    [Test]
    public async Task GlobalAdmissionClaimsOrderedBoundedIdentitiesWithoutPrivateTableAuthority()
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        DateTime now = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now;
        TimeSpan duration = TimeSpan.FromMinutes(5);
        const string owner = "pds-admission-regression";
        var pending = Outbox(
            Guid.Parse("00000000-0000-7000-8000-000000000011"),
            rows.A, rows.UserId, now.AddMinutes(-3));
        var expired = Outbox(
            Guid.Parse("00000000-0000-7000-8000-000000000012"),
            rows.B, rows.OtherUserId, pending.CreatedAt);
        Guid oldToken = Guid.CreateVersion7();
        expired.Status = PdsSyncStatus.Processing;
        expired.LeaseOwner = "previous-worker";
        expired.LeaseToken = oldToken;
        expired.LeaseExpiresAt = now;
        expired.LeaseFence = 7;
        var remainder = Outbox(Guid.CreateVersion7(), rows.B, rows.UserId, now.AddMinutes(-2));
        var notDue = Outbox(Guid.CreateVersion7(), rows.A, rows.UserId, now.AddMinutes(-5));
        notDue.NextRetryAt = now.AddHours(1);
        var unresolved = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = $"did:plc:{Guid.CreateVersion7():N}",
            Collection = "app.bsky.feed.post",
            RecordKey = "unresolved",
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            UpdatedAt = now
        };
        var blocked = Outbox(Guid.CreateVersion7(), rows.B, rows.OtherUserId, now.AddMinutes(-4));
        blocked.DependsOnAtprotoRecordId = unresolved.Id;
        string table;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AtprotoRecords.Add(unresolved);
            seed.PdsSyncOutbox.AddRange(pending, expired, remainder, notDue, blocked);
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(PdsSyncOutbox))!;
            table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }

        // This existing helper creates a NOSUPERUSER/NOBYPASSRLS Global login,
        // with bounded admission execution, not private PDS table grants.
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresPdsQueueAdmissionContract.GlobalRole);
        await using var global = await authority.Factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await AssertPrivateReadDeniedAsync(global, table);
        var admission = new PdsSyncQueueAdmission(authority.Factory, authority.Factory);

        IReadOnlyList<PdsSyncClaim> claims = await admission.ClaimDueAsync(2, owner, now, duration);

        await Assert.That(claims.Count).IsEqualTo(2);
        await AssertClaimAsync(claims[0], pending, 1);
        await AssertClaimAsync(claims[1], expired, 8);
        await Assert.That(claims[1].LeaseToken).IsNotEqualTo(oldToken);
        await Assert.That(claims[0].LeaseToken).IsNotEqualTo(claims[1].LeaseToken);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.ChangeTracker.Entries<PdsSyncOutbox>().Any()).IsFalse();
        await AssertPrivateReadDeniedAsync(global, table);

        // The fixture connection is the migration authority, not the Global
        // worker. It observes committed state without extending worker grants.
        await using var oracle = new NpgsqlConnection(fixture.ConnectionString);
        await oracle.OpenAsync();
        await AssertPersistedClaimAsync(oracle, table, claims[0], owner, now.Add(duration));
        await AssertPersistedClaimAsync(oracle, table, claims[1], owner, now.Add(duration));
        foreach (PdsSyncOutbox control in new[] { remainder, notDue, blocked })
        {
            await using var command = oracle.CreateCommand();
            command.CommandText = $"""
                SELECT status, lease_owner, lease_token, lease_expires_at, lease_fence, next_retry_at
                FROM {table} WHERE id = @id
                """;
            command.Parameters.AddWithValue("id", control.Id);
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo((int)PdsSyncStatus.Pending);
            await Assert.That(reader.IsDBNull(1)).IsTrue();
            await Assert.That(reader.IsDBNull(2)).IsTrue();
            await Assert.That(reader.IsDBNull(3)).IsTrue();
            await Assert.That(reader.GetInt64(4)).IsEqualTo(0);
            await Assert.That(reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5))
                .IsEqualTo(control.NextRetryAt);
        }

        // Advance the supplied clock, not wall time: reclaiming the same oldest
        // rows at lease expiry must replace their tokens and increment both fences.
        IReadOnlyList<PdsSyncClaim> reclaimed = await admission.ClaimDueAsync(
            2, owner, now.Add(duration), duration);
        await Assert.That(reclaimed.Count).IsEqualTo(2);
        await AssertClaimAsync(reclaimed[0], pending, 2);
        await AssertClaimAsync(reclaimed[1], expired, 9);
        for (int index = 0; index < reclaimed.Count; index++)
        {
            await Assert.That(reclaimed[index].LeaseToken).IsNotEqualTo(claims[index].LeaseToken);
            await AssertPersistedClaimAsync(
                oracle, table, reclaimed[index], owner, now.Add(duration).Add(duration));
        }
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await AssertPrivateReadDeniedAsync(global, table);
    }

    [Test]
    public async Task GlobalAdmissionWithoutPurposeExecutionFailsClosed()
    {
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        var admission = new PdsSyncQueueAdmission(authority.Factory, authority.Factory);
        PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await admission.ClaimDueAsync(
                1, "pds-admission-denied",
                PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now,
                TimeSpan.FromMinutes(5));
        });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NativeAdmissionRejectsForgedBatchOrEmptyLeaseToken(bool oversizedBatch)
    {
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresPdsQueueAdmissionContract.GlobalRole);
        await using var global = await authority.Factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var entity = global.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        string function = global.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_admit", entity.GetSchema() ?? "public");
        DateTime now = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now;
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {function}(@take, @owner, @claimed, @expires, @tokens)";
        var native = (NpgsqlCommand)command;
        native.Parameters.AddWithValue("take", oversizedBatch ? 101 : 1);
        native.Parameters.AddWithValue("owner", "pds-admission-forged");
        native.Parameters.AddWithValue("claimed", now);
        native.Parameters.AddWithValue("expires", now.AddMinutes(5));
        native.Parameters.AddWithValue("tokens", new[] { oversizedBatch ? Guid.CreateVersion7() : Guid.Empty });

        PostgresException? rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var reader = await command.ExecuteReaderAsync();
        });

        await Assert.That(rejected?.SqlState).IsEqualTo("22023");
        await Assert.That(global.TenantFilterTenantId).IsNull();
    }

    private static PdsSyncOutbox Outbox(Guid id, Guid tenantId, Guid userId, DateTime createdAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        UserId = userId,
        Did = $"did:plc:{userId:N}",
        Collection = "app.bsky.feed.post",
        RecordKey = id.ToString("N"),
        Operation = PdsSyncOperation.Create,
        Payload = "{}",
        PayloadHash = new string('a', 64),
        IdempotencyKey = id.ToString("N"),
        PdsHost = "https://pds.example.test",
        SourceEntityType = "Event",
        SourceEntityId = Guid.CreateVersion7(),
        SourceVersion = Guid.CreateVersion7(),
        Status = PdsSyncStatus.Pending,
        CreatedAt = createdAt,
        MaxRetries = 10
    };

    private static async Task AssertClaimAsync(PdsSyncClaim claim, PdsSyncOutbox expected, long fence)
    {
        await Assert.That(claim.OutboxId).IsEqualTo(expected.Id);
        await Assert.That(claim.TenantId).IsEqualTo(expected.TenantId);
        await Assert.That(claim.UserId).IsEqualTo(expected.UserId);
        await Assert.That(claim.LeaseToken).IsNotEqualTo(Guid.Empty);
        await Assert.That(claim.LeaseFence).IsEqualTo(fence);
    }

    private static async Task AssertPrivateReadDeniedAsync(ExploreDbContext global, string table)
    {
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT 1 FROM {table} LIMIT 1";
        PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await command.ExecuteScalarAsync();
        });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    private static async Task AssertPersistedClaimAsync(
        NpgsqlConnection oracle, string table, PdsSyncClaim claim, string owner, DateTime expiresAt)
    {
        await using var command = oracle.CreateCommand();
        command.CommandText = $"""
            SELECT tenant_id, user_id, status, lease_owner, lease_token, lease_expires_at, lease_fence
            FROM {table} WHERE id = @id
            """;
        command.Parameters.AddWithValue("id", claim.OutboxId);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetGuid(0)).IsEqualTo(claim.TenantId);
        await Assert.That(reader.GetGuid(1)).IsEqualTo(claim.UserId);
        await Assert.That(reader.GetInt32(2)).IsEqualTo((int)PdsSyncStatus.Processing);
        await Assert.That(reader.GetString(3)).IsEqualTo(owner);
        await Assert.That(reader.GetGuid(4)).IsEqualTo(claim.LeaseToken);
        await Assert.That(reader.GetDateTime(5)).IsEqualTo(expiresAt);
        await Assert.That(reader.GetInt64(6)).IsEqualTo(claim.LeaseFence);
    }
}
