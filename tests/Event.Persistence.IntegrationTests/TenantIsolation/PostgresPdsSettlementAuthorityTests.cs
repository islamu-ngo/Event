using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresPdsSettlementAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(Scenario.Commit)]
    [Arguments(Scenario.Rollback)]
    [Arguments(Scenario.StaleClaim)]
    [Arguments(Scenario.ForeignClaim)]
    [Arguments(Scenario.ZeroLease)]
    [Arguments(Scenario.MissingCapability)]
    [Arguments(Scenario.WrongPrivilege)]
    [Arguments(Scenario.MissingTenant)]
    [Arguments(Scenario.AmbientTransaction)]
    public async Task MixedSettlementCommitsOwnClaimSharedRecordAndSuppressedTenantEpoch(Scenario scenario)
    {
        await fixture.ResetAsync();
        DateTime now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid ownershipVersion = Guid.CreateVersion7();
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = "did:plc:" + userId.ToString("N")[..24],
            Collection = "community.lexicon.calendar.event",
            RecordKey = "settlement",
            Cid = "old-cid",
            RecordJson = "{\"name\":\"old\"}",
            RecordHash = new string('a', 64),
            SourceVersion = 1,
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            UpdatedAt = now
        };
        record.Uri = $"at://{record.Did}/{record.Collection}/{record.RecordKey}";
        var outbox = new PdsSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantA,
            UserId = userId,
            Did = record.Did,
            Collection = record.Collection,
            RecordKey = record.RecordKey,
            Operation = PdsSyncOperation.Update,
            ExpectedCid = record.Cid,
            Payload = "{\"name\":\"new\"}",
            PayloadHash = new string('b', 64),
            IdempotencyKey = Guid.CreateVersion7().ToString("N"),
            PdsHost = "https://pds.example.test",
            SourceEntityType = "Event",
            SourceEntityId = Guid.CreateVersion7(),
            SourceVersion = Guid.CreateVersion7(),
            AtprotoRecordId = record.Id,
            Status = PdsSyncStatus.Processing,
            CreatedAt = now,
            MaxRetries = 5,
            LeaseOwner = "settlement",
            LeaseToken = Guid.CreateVersion7(),
            LeaseFence = 1,
            LeaseExpiresAt = now.AddMinutes(5)
        };
        string schema;
        string[] tables;
        string revisionTable;
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresPdsSettlementContract.ApplyAsync(seed);
            seed.Tenants.AddRange(Tenant(tenantA), Tenant(tenantB));
            seed.Users.Add(new User
            {
                Id = userId,
                Pii = new UserPii { Email = "settlement@example.test", FirstName = "Settlement", LastName = "Owner" },
                ConcurrencyStamp = Guid.CreateVersion7(),
                CreatedAt = now
            });
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantA,
                UserId = userId,
                Tenant = null!,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now,
                CreatedAt = now
            });
            await seed.SaveChangesAsync();
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Settlement owner" },
                CreatedAt = now
            };
            seed.AddRange(actor, new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = outbox.SourceEntityId,
                TenantId = tenantA,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                Title = "Settlement source",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
                CreatedAt = now,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            seed.AddRange(record, outbox, new AtprotoOutboundRecordOwnership
            {
                AtprotoRecordId = record.Id,
                TenantId = tenantA,
                UserId = userId,
                SourceEntityType = outbox.SourceEntityType,
                SourceEntityId = outbox.SourceEntityId,
                SourceVersion = ownershipVersion,
                CreatedAt = now,
                UpdatedAt = now
            });
            seed.AtprotoRecordTenantPresentations.AddRange(
                new() { TenantId = tenantA, AtprotoRecordId = record.Id, IsVisible = true, SourceVersion = 1, EvaluatedAt = now },
                new() { TenantId = tenantB, AtprotoRecordId = record.Id, IsVisible = false, SourceVersion = 1, EvaluatedAt = now });
            await seed.SaveChangesAsync();
            schema = seed.Model.FindEntityType(typeof(PdsSyncOutbox))!.GetSchema()!;
            revisionTable = $"\"{schema}\".\"{seed.Model.FindEntityType(typeof(EventDiscoveryRevision))!.GetTableName()}\"";
            tables = new[] { typeof(PdsSyncOutbox), typeof(AtprotoRecord), typeof(AtprotoOutboundRecordOwnership),
                typeof(AtprotoRecordTenantPresentation), typeof(Explore.Domain.Event) }
                .Select(type => $"\"{schema}\".\"{seed.Model.FindEntityType(type)!.GetTableName()}\"").ToArray();
        }
        Dictionary<Guid, long> before;
        await using (var baseline = fixture.CreateDbContext())
            before = await baseline.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                .Where(row => row.TenantId == tenantA || row.TenantId == tenantB)
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
        string role = "settlement_" + Guid.CreateVersion7().ToString("N");
        string globalRole = role + "_global";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using var grant = admin.CreateCommand();
        grant.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
            CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON {string.Join(", ", tables)} TO "{role}";
            """;
        await grant.ExecuteNonQueryAsync();
        if (scenario != Scenario.MissingCapability)
        {
            grant.CommandText = $"GRANT {PostgresPdsSettlementContract.GlobalRole} TO \"{globalRole}\"";
            await grant.ExecuteNonQueryAsync();
        }
        if (scenario == Scenario.WrongPrivilege)
        {
            grant.CommandText = $"REVOKE UPDATE (cid) ON {tables[1]} FROM {PostgresPdsSettlementContract.GlobalRole}";
            await grant.ExecuteNonQueryAsync();
        }
        string trigger = "settlement_failure_" + Guid.CreateVersion7().ToString("N");
        if (scenario == Scenario.Rollback)
        {
            grant.CommandText = $"""
                CREATE FUNCTION "{schema}"."{trigger}"() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF NEW.tenant_id = '{tenantA}'::uuid THEN
                        RAISE EXCEPTION 'injected_settlement_precommit_failure' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "{trigger}" BEFORE UPDATE ON {revisionTable}
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."{trigger}"();
                """;
            await grant.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Username = role,
            Password = password,
            Schema = schema,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
        string? pooledConnection = null;
        string? globalConnection = null;
        try
        {
            var factory = new TenantExploreDbContextFactory(options, poolSize: 1);
            await using var writer = await factory.CreateDbContextAsync(tenantA);
            pooledConnection = writer.Database.GetConnectionString();
            var globalOptions = options with
            {
                Role = PrimaryDatabaseRole.Global,
                Username = globalRole
            };
            globalConnection = PrimaryDatabaseConfiguration.BuildConnectionString(globalOptions).ConnectionString;
            var global = new GlobalExploreDbContextFactory(globalOptions, poolSize: 1);
            IPdsSettlementDbContextFactory settlementFactory = global;
            await writer.Database.OpenConnectionAsync();
            await using (var ordinary = writer.Database.GetDbConnection().CreateCommand())
            {
                ordinary.CommandText = $"""
                    SELECT pg_has_role(current_user, '{PostgresPdsSettlementContract.GlobalRole}', 'USAGE')
                        OR pg_has_role(current_user, '{PostgresDiscoveryDisclosureContract.RuntimeRole}', 'USAGE');
                    """;
                await Assert.That((bool)(await ordinary.ExecuteScalarAsync())!).IsFalse();
            }
            bool ready = scenario is not (Scenario.MissingCapability or Scenario.WrongPrivilege);
            if (ready)
            {
                await using var lease = await settlementFactory.CreateDbContextAsync(tenantA);
                globalConnection = lease.Database.GetConnectionString();
                await Assert.That(lease.TenantFilterTenantId).IsEqualTo(tenantA);
                await using (var authority = lease.Database.GetDbConnection().CreateCommand())
                {
                    authority.CommandText = $"""
                        SELECT current_user,
                            has_table_privilege(current_user, '{tables[0]}', 'TRUNCATE'),
                            has_table_privilege(current_user, '{tables[0]}', 'DELETE'),
                            has_schema_privilege(current_user, '{schema}', 'CREATE'),
                            pg_has_role(current_user, '{PostgresDiscoveryDisclosureContract.OwnerRole}', 'MEMBER'),
                            pg_has_role(current_user, '{PostgresDiscoveryDisclosureContract.MigratorRole}', 'MEMBER');
                        """;
                    await using var reader = await authority.ExecuteReaderAsync();
                    await Assert.That(await reader.ReadAsync()).IsTrue();
                    await Assert.That(reader.GetString(0)).IsEqualTo(globalRole);
                    for (int index = 1; index <= 5; index++)
                        await Assert.That(reader.GetBoolean(index)).IsFalse();
                }
                await Assert.That(await lease.AtprotoRecordTenantPresentations.IgnoreQueryFilters()
                    .CountAsync(row => row.AtprotoRecordId == record.Id)).IsEqualTo(1);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                {
                    lease.TenantContext = new TenantIdentity(tenantB);
                    return Task.CompletedTask;
                });
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                {
                    await using var zero = await settlementFactory.CreateDbContextAsync(Guid.Empty);
                });
                await using var lookup = await global.CreateDbContextAsync();
                await Assert.That(lookup.TenantFilterTenantId).IsNull();
                await Assert.That(await lookup.PdsSyncOutbox.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                {
                    lookup.TenantContext = new TenantIdentity(tenantA);
                    return Task.CompletedTask;
                });
            }
            var claim = new PdsSyncClaim(outbox.Id,
                scenario == Scenario.ForeignClaim ? tenantB : tenantA, userId,
                scenario == Scenario.StaleClaim ? Guid.CreateVersion7()
                    : scenario == Scenario.ZeroLease ? Guid.Empty : outbox.LeaseToken!.Value, 1);
            bool settled = false;
            var repository = new PdsSyncOutboxRepository(writer, settlementFactory);
            if (!ready || scenario == Scenario.Rollback)
            {
                Exception? rejected = null;
                try
                {
                    settled = await repository.TrySettleAsync(claim, record.Uri, "new-cid", now.AddSeconds(1));
                }
                catch (Exception exception) when (exception is InvalidOperationException or PostgresException or DbUpdateException)
                {
                    rejected = exception;
                }
                await Assert.That(rejected).IsNotNull();
                if (scenario == Scenario.Rollback)
                    await Assert.That(((PostgresException)rejected!).SqlState).IsEqualTo("23514");
                else
                    await Assert.That(rejected is InvalidOperationException
                    { Message: "pds_settlement_authority_not_ready" or "postgres_tenant_runtime_authority_not_ready" })
                        .IsTrue();
            }
            else if (scenario == Scenario.MissingTenant)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await using var missing = await settlementFactory.CreateDbContextAsync(Guid.CreateVersion7());
                });
            }
            else if (scenario == Scenario.AmbientTransaction)
            {
                await writer.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await writer.Database.BeginTransactionAsync();
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repository.TrySettleAsync(claim, record.Uri, "new-cid", now.AddSeconds(1)));
                    await transaction.RollbackAsync();
                });
            }
            else
            {
                settled = await repository.TrySettleAsync(claim, record.Uri, "new-cid", now.AddSeconds(1));
            }
            bool committed = scenario == Scenario.Commit;
            await Assert.That(settled).IsEqualTo(committed);
            await using var verify = fixture.CreateDbContext();
            var persistedClaim = await verify.PdsSyncOutbox.SingleAsync(row => row.Id == outbox.Id);
            await Assert.That(persistedClaim.Status).IsEqualTo(committed ? PdsSyncStatus.Completed : PdsSyncStatus.Processing);
            await Assert.That(persistedClaim.LeaseToken).IsEqualTo(committed ? null : outbox.LeaseToken);
            await Assert.That((await verify.AtprotoRecords.SingleAsync(row => row.Id == record.Id)).Cid)
                .IsEqualTo(committed ? "new-cid" : "old-cid");
            using var actualPayload = JsonDocument.Parse(
                (await verify.AtprotoRecords.SingleAsync(row => row.Id == record.Id)).RecordJson!);
            using var expectedPayload = JsonDocument.Parse(committed ? "{\"name\":\"new\"}" : "{\"name\":\"old\"}");
            await Assert.That(JsonElement.DeepEquals(actualPayload.RootElement, expectedPayload.RootElement)).IsTrue();
            await Assert.That((await verify.Events.SingleAsync(row => row.Id == outbox.SourceEntityId)).AtprotoRecordId)
                .IsEqualTo(committed ? record.Id : null);
            await Assert.That((await verify.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters()
                .SingleAsync(row => row.AtprotoRecordId == record.Id)).SourceVersion)
                .IsEqualTo(committed ? outbox.SourceVersion : ownershipVersion);
            await Assert.That((await verify.AtprotoRecordTenantPresentations.IgnoreQueryFilters()
                .SingleAsync(row => row.TenantId == tenantB && row.AtprotoRecordId == record.Id)).IsVisible).IsFalse();
            var after = await verify.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                .Where(row => row.TenantId == tenantA || row.TenantId == tenantB)
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
            await Assert.That(after[tenantA]).IsEqualTo(before[tenantA] + (committed ? 1 : 0));
            await Assert.That(after[tenantB]).IsEqualTo(before[tenantB] + (committed ? 1 : 0));
        }
        finally
        {
            if (pooledConnection is not null)
            {
                using var pool = new NpgsqlConnection(pooledConnection);
                NpgsqlConnection.ClearPool(pool);
            }
            if (globalConnection is not null)
            {
                using var pool = new NpgsqlConnection(globalConnection);
                NpgsqlConnection.ClearPool(pool);
            }
            if (scenario == Scenario.Rollback)
            {
                grant.CommandText = $"""
                    DROP TRIGGER "{trigger}" ON {revisionTable};
                    DROP FUNCTION "{schema}"."{trigger}"();
                    """;
                await grant.ExecuteNonQueryAsync();
            }
            grant.CommandText = $"""
                REVOKE SELECT, INSERT, UPDATE ON {string.Join(", ", tables)} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                DROP ROLE "{globalRole}";
                """;
            await grant.ExecuteNonQueryAsync();
            await using var restore = fixture.CreateDbContext();
            await PostgresPdsSettlementContract.ApplyAsync(restore);
        }
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        Slug = "settlement-" + id.ToString("N"),
        FullName = "Settlement",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;

    public enum Scenario
    {
        Commit, Rollback, StaleClaim, ForeignClaim, ZeroLease, MissingCapability, WrongPrivilege, MissingTenant,
        AmbientTransaction
    }
}
