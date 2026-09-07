using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Onboarding;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class InstanceBootstrapStatePersistenceTests(PostgreSqlContainerFixture fixture)
{
    private const string ConfigurationFingerprint =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string SelectorFingerprint =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private static readonly DateTime CreatedAt =
        new(2026, 8, 31, 10, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = CreatedAt.AddMinutes(3);

    [Test]
    public async Task ConfiguredCompletion_RoundTripsTypedGenerationAndLocalUserLineage()
    {
        await fixture.ResetAsync();
        Guid userId = Guid.CreateVersion7();
        Guid stateId = Guid.CreateVersion7();

        await using (ExploreDbContext write = fixture.CreateDbContext())
        {
            write.Users.Add(CreateUser(userId));
            var repository = new InstanceBootstrapStateRepository(write);
            InstanceBootstrapState state = InstanceBootstrapState.CreateConfiguredAdministratorPending(
                stateId,
                AuthenticationProviderKind.Atproto,
                DeploymentMode.MultiTenant,
                generation: 17,
                ConfigurationFingerprint,
                SelectorFingerprint,
                CreatedAt);
            await repository.Create(state);
            await Assert.That(state.CompleteConfiguredAdministrator(
                AuthenticationProviderKind.Atproto,
                generation: 17,
                SelectorFingerprint,
                userId,
                CompletedAt)).IsTrue();
            await repository.Update(state);
        }

        await using ExploreDbContext read = fixture.CreateDbContext();
        InstanceBootstrapState persisted = await read.InstanceBootstrapStates
            .AsNoTracking()
            .SingleAsync(state => state.Id == stateId);

        await Assert.That(persisted.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(persisted.Mode).IsEqualTo(InstanceBootstrapMode.ConfiguredAdministrator);
        await Assert.That(persisted.ProviderKind).IsEqualTo(AuthenticationProviderKind.Atproto);
        await Assert.That(persisted.DeploymentMode).IsEqualTo(DeploymentMode.MultiTenant);
        await Assert.That(persisted.Generation).IsEqualTo(17L);
        await Assert.That(persisted.ConfigurationFingerprint).IsEqualTo(ConfigurationFingerprint);
        await Assert.That(persisted.SelectorFingerprint).IsEqualTo(SelectorFingerprint);
        await Assert.That(persisted.CompletedIdentityFingerprint).IsEqualTo(SelectorFingerprint);
        await Assert.That(persisted.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(persisted.CompletedAt).IsEqualTo(CompletedAt);
        await Assert.That(persisted.CompletedByUserId).IsEqualTo(userId);
        await Assert.That(persisted.SupersededAt).IsNull();
        await Assert.That(persisted.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(persisted.CompletedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(await read.Users.AnyAsync(user => user.Id == userId)).IsTrue();
    }

    [Test]
    public async Task MigratedSchema_UsesFixedFingerprintsAndContainsNoLegacyBinaryColumns()
    {
        await fixture.ResetAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'instance_bootstrap_states'
            ORDER BY ordinal_position
            """;

        var columns = new Dictionary<string, (string DataType, int? Length)>(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(
                reader.GetString(0),
                (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        string[] typedColumns =
        [
            "status", "mode", "provider_kind", "deployment_mode", "generation",
            "configuration_fingerprint", "selector_fingerprint",
            "completed_identity_fingerprint", "created_at", "superseded_at",
            "completed_at", "completed_by_user_id"
        ];
        await Assert.That(typedColumns.All(columns.ContainsKey)).IsTrue();
        await Assert.That(columns.ContainsKey("is_completed")).IsFalse();
        await Assert.That(columns.ContainsKey("selected_deployment_mode")).IsFalse();

        foreach (string fingerprintColumn in new[]
                 {
                     "configuration_fingerprint",
                     "selector_fingerprint",
                     "completed_identity_fingerprint"
                 })
        {
            await Assert.That(columns[fingerprintColumn].DataType).IsEqualTo("character");
            await Assert.That(columns[fingerprintColumn].Length).IsEqualTo(64);
        }
    }

    [Test]
    public async Task GetCurrent_OrdersByCreatedAtThenIdentifierDeterministically()
    {
        await fixture.ResetAsync();
        Guid earlierId = Guid.Parse("01996d98-0000-7000-8000-000000000001");
        Guid lowerTieId = Guid.Parse("01996d98-0000-7000-8000-000000000002");
        Guid higherTieId = Guid.Parse("01996d98-0000-7000-8000-000000000003");

        await using (ExploreDbContext write = fixture.CreateDbContext())
        {
            write.InstanceBootstrapStates.AddRange(
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    earlierId, AuthenticationProviderKind.Keycloak,
                    DeploymentMode.SingleTenant, 1, ConfigurationFingerprint,
                    SelectorFingerprint, CreatedAt.AddMinutes(-1)),
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    lowerTieId, AuthenticationProviderKind.Keycloak,
                    DeploymentMode.SingleTenant, 2, ConfigurationFingerprint,
                    SelectorFingerprint, CreatedAt),
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    higherTieId, AuthenticationProviderKind.Keycloak,
                    DeploymentMode.SingleTenant, 3, ConfigurationFingerprint,
                    SelectorFingerprint, CreatedAt));
            await write.SaveChangesAsync();
        }

        await using ExploreDbContext read = fixture.CreateDbContext();
        InstanceBootstrapState? current =
            await new InstanceBootstrapStateRepository(read).GetCurrent();

        await Assert.That(current).IsNotNull();
        await Assert.That(current!.Id).IsEqualTo(higherTieId);
        await Assert.That(current.Generation).IsEqualTo(3L);
    }

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        Pii = new UserPii
        {
            Email = $"bootstrap-{userId:N}@example.test",
            FirstName = "Bootstrap",
            LastName = "Administrator"
        },
        EmailVerified = true,
        ConcurrencyStamp = Guid.CreateVersion7(),
        CreatedAt = CreatedAt
    };
}
