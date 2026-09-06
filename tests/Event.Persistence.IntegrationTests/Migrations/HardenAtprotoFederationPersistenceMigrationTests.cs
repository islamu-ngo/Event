// ABOUTME: Verifies hardened AT Protocol federation persistence in the rebased PostgreSQL baseline.
// ABOUTME: Covers the final tables, constraints, and source-version uniqueness without deleted history boundaries.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoFederationBaselineGuardTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_ContainsHardenedFederationSchemaAndGuards()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() " +
            "AND table_name IN ('atproto_records', 'pds_sync_outbox', 'atproto_jetstream_consumer_states', " +
            "'atproto_jetstream_quarantines', 'atproto_record_tenant_presentations', 'atproto_outbound_record_ownerships')"))
            .IsEqualTo(6L);
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname IN " +
            "('ck_atproto_records_direction', 'ck_atproto_records_provenance', " +
            "'ck_pds_sync_outbox_operation', 'ck_pds_sync_outbox_status', 'ck_pds_sync_outbox_payload_shape')"))
            .IsEqualTo(5L);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        string recordIdentityIndex = FindUniqueIndex(
            model,
            typeof(AtprotoRecord),
            nameof(AtprotoRecord.Did),
            nameof(AtprotoRecord.Collection),
            nameof(AtprotoRecord.RecordKey));
        string sourceVersionIndex = FindUniqueIndex(
            model,
            typeof(PdsSyncOutbox),
            nameof(PdsSyncOutbox.TenantId),
            nameof(PdsSyncOutbox.SourceEntityType),
            nameof(PdsSyncOutbox.SourceEntityId),
            nameof(PdsSyncOutbox.SourceVersion),
            nameof(PdsSyncOutbox.Operation),
            nameof(PdsSyncOutbox.PayloadHash));
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() " +
            "AND indexname IN (@record_identity, @source_version)",
            ("record_identity", recordIdentityIndex),
            ("source_version", sourceVersionIndex)))
             .IsEqualTo(2L);
    }

    [Test]
    public async Task CurrentBaseline_GlobalizesActorLifecycleWithCaseSensitiveDidCustody()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() " +
            "AND table_name IN ('atproto_identities', 'actor_merges', 'external_actor_subjects', " +
            "'organization_tenants', 'group_tenants', 'event_public_actions', 'event_provenance_types')"))
            .IsEqualTo(7L);
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname IN " +
            "('fk_atproto_identities_did_custody_types_did_custody_type_id', " +
            "'ck_actors_exactly_one_owner', 'ck_pds_sync_outbox_payload_shape')"))
            .IsEqualTo(3L);
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM pg_attribute attribute_entry " +
            "JOIN pg_class table_entry ON table_entry.oid = attribute_entry.attrelid " +
            "JOIN pg_collation collation_entry ON collation_entry.oid = attribute_entry.attcollation " +
            "WHERE table_entry.relname = 'atproto_identities' " +
            "AND attribute_entry.attname = 'did' AND collation_entry.collname = 'C'"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task CurrentBaseline_RejectsDuplicateExternalProviderIdentityAcrossTenants()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = CreateTenant("provider-identity-a");
        var tenantB = CreateTenant("provider-identity-b");
        var userA = CreateUser("provider-identity-a");
        var userB = CreateUser("provider-identity-b");
        context.AddRange(tenantA, tenantB, userA, userB);
        await context.SaveChangesAsync();

        var providerKey = "did:plc:" + new string('a', 2040);
        context.UserExternalLogins.Add(CreateExternalLogin(tenantA.Id, userA.Id, providerKey));
        await context.SaveChangesAsync();
        context.UserExternalLogins.Add(CreateExternalLogin(tenantB.Id, userB.Id, providerKey));

        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
        await Assert.That(await ReadCountAsync(
            "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() " +
            "AND indexname = 'ix_user_external_logins_authentication_provider_id_provider_key'"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task CurrentBaseline_HasTheCompleteAppliedApplicationMigrationChain()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        string[] available = context.Database.GetMigrations().ToArray();
        string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await Assert.That(available.Count(migration =>
                migration.EndsWith("_Init", StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That(available[0]).EndsWith("_Init");
        await Assert.That(applied).IsEquivalentTo(available);
    }

    private async Task<long> ReadCountAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string FindUniqueIndex(
        IModel model,
        Type entityType,
        params string[] propertyNames)
    {
        var entity = model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"No EF metadata exists for {entityType.Name}.");
        return entity.GetIndexes()
            .Single(index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(propertyNames))
            .GetDatabaseName();
    }

    private static Tenant CreateTenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slug,
        Slug = slug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static User CreateUser(string emailPrefix) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"{emailPrefix}@example.com",
            FirstName = "Provider",
            LastName = "Identity"
        },
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow
    };

    private static UserExternalLogin CreateExternalLogin(Guid tenantId, Guid userId, string providerKey) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Atproto,
        AuthenticationProvider = null!,
        ProviderKey = providerKey,
        ProviderDisplayName = "ATProto",
        CreatedAt = DateTime.UtcNow
    };
}
