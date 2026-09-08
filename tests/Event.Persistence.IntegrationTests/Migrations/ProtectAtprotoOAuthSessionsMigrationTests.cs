using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoOAuthSessionBaselineTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_ContainsOnlyOAuthSessionGuardsAndUniqueSubjectIndex()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .Contains(migration => migration.EndsWith("_Init", StringComparison.Ordinal));
        string[] constraintNames = await ReadConstraintNamesAsync();
        await Assert.That(constraintNames).Contains("ck_user_authentication_tokens_ciphertext_not_empty");
        await Assert.That(constraintNames).Contains("ck_user_authentication_tokens_envelope_version");
        await Assert.That(constraintNames).Contains("ck_user_authentication_tokens_required_text");
        string subjectIndex = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(UserAuthenticationToken))!
            .GetIndexes()
            .Single(index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(UserAuthenticationToken.TenantId),
                        nameof(UserAuthenticationToken.Provider),
                        nameof(UserAuthenticationToken.SubjectDid)]))
            .GetDatabaseName();
        await Assert.That(await IndexExistsAsync(subjectIndex)).IsTrue();
    }

    [Test]
    public async Task CurrentBaseline_RejectsInvalidCiphertextAndDuplicateSubject()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenantId, userId) = await SeedScopeAsync(context);

        await InsertEncryptedSessionAsync(tenantId, userId, Enumerable.Repeat((byte)1, 29).ToArray());
        var invalidCiphertext = await Assert.That(async () =>
                await InsertEncryptedSessionAsync(tenantId, userId, []))
            .Throws<PostgresException>();
        await Assert.That(invalidCiphertext!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);

        var duplicateSubject = await Assert.That(async () =>
                await InsertEncryptedSessionAsync(tenantId, userId, Enumerable.Repeat((byte)2, 29).ToArray()))
            .Throws<PostgresException>();
        await Assert.That(duplicateSubject!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
    }

    private static async Task<(Guid TenantId, Guid UserId)> SeedScopeAsync(ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.SingleAsync(status =>
            status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "ATProto migration",
            Slug = $"atproto-migration-{Guid.NewGuid():N}"[..32],
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"atproto-migration-{Guid.NewGuid():N}@example.test",
                FirstName = "ATProto",
                LastName = "Migration"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        return (tenant.Id, user.Id);
    }

    private async Task InsertEncryptedSessionAsync(Guid tenantId, Guid userId, byte[] ciphertext)
    {
        const string sql =
            """
            INSERT INTO user_authentication_tokens
                (id, user_id, tenant_id, provider, subject_did, session_ciphertext, encryption_key_id,
                 o_auth_client_key_id, envelope_version, concurrency_stamp, pds_host, created_at)
            VALUES
                (@id, @user_id, @tenant_id, 'atproto', 'did:plc:migration', @ciphertext, 'active-key',
                 'oauth-client-key', 1, @stamp, 'https://pds.example/', NOW())
            """;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("ciphertext", ciphertext);
        command.Parameters.AddWithValue("stamp", Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> ReadConstraintNamesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint WHERE conrelid = 'user_authentication_tokens'::regclass",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = current_schema() AND indexname = @name)",
            connection);
        command.Parameters.AddWithValue("name", indexName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
