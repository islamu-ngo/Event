using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class OrganizerPaymentConnectionNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddMinutes(-30);

    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 2)]
    [Arguments(10, 3)]
    public async Task NativeReadinessDiscoveryPreservesCrossTenantPredicatesAndBoundedOrder(int limit, int expectedCount)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        Scope first = await SeedScopeAsync(seed);
        Scope foreign = await SeedScopeAsync(seed);
        OrganizerPaymentProviderConnection oldest = Connection(foreign, "oldest", Now.AddDays(-3));
        OrganizerPaymentProviderConnection pending = Connection(first, "pending", Now.AddDays(-2));
        OrganizerPaymentProviderConnection stale = Connection(foreign, "stale", Now.AddDays(-1));
        stale.ApplyReadiness(Observation(Cutoff.AddSeconds(-1), ready: false));
        OrganizerPaymentProviderConnection boundary = Connection(first, "boundary", Now.AddDays(-1));
        boundary.ApplyReadiness(Observation(Cutoff, ready: false));
        OrganizerPaymentProviderConnection fresh = Connection(foreign, "fresh", Now.AddDays(-1));
        fresh.ApplyReadiness(Observation(Cutoff.AddSeconds(1), ready: false));
        OrganizerPaymentProviderConnection ready = Connection(first, "ready", Now.AddDays(-1));
        ready.ApplyReadiness(Observation(Cutoff.AddSeconds(-1), ready: true));
        OrganizerPaymentProviderConnection disabled = Connection(foreign, "disabled", Now.AddDays(-1));
        disabled.Disable("operator_disabled", Now);
        OrganizerPaymentProviderConnection deleted = Connection(first, "deleted", Now.AddDays(-1));
        deleted.IsDeleted = true;
        deleted.DeletedAt = Now;
        OrganizerPaymentProviderConnection replaced = Connection(foreign, "replaced", Now.AddDays(-1));
        OrganizerPaymentProviderConnection replacement = replaced.ReplaceWith(
            Guid.CreateVersion7(), $"acct_{Guid.CreateVersion7():N}", Now.AddHours(-1));
        replacement.ApplyReadiness(Observation(Now, ready: true));
        seed.AddRange(oldest, pending, stale, boundary, fresh, ready, disabled, deleted, replaced, replacement);
        await seed.SaveChangesAsync();
        replaced.MarkReplacedBy(replacement.Id);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        await Assert.That(await runtime.OrganizerPaymentProviderConnections.AnyAsync(row => row.Id == pending.Id)).IsTrue();
        await Assert.That(await runtime.OrganizerPaymentProviderConnections.AnyAsync(row => row.Id == oldest.Id)).IsFalse();
        var repository = new OrganizerPaymentProviderConnectionRepository(runtime);

        IReadOnlyList<OrganizerPaymentProviderConnection> due =
            await repository.ListDueReadinessChecksAsync(Cutoff, limit, CancellationToken.None);

        await Assert.That(runtime.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(due.Count).IsEqualTo(expectedCount);
        Guid[] expected = [oldest.Id, pending.Id, stale.Id];
        await Assert.That(due.Select(row => row.Id).SequenceEqual(expected.Take(expectedCount))).IsTrue();
        foreach (OrganizerPaymentProviderConnection row in due)
        {
            Scope owner = row.Id == pending.Id ? first : foreign;
            await Assert.That(row.TenantId).IsEqualTo(owner.TenantId);
            await Assert.That(row.OrganizerActorId).IsEqualTo(owner.ActorId);
            await Assert.That(row.ProviderCode).IsEqualTo("stripe");
        }
        if (limit == 10)
        {
            await Assert.That(due[0].StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
            await Assert.That(due[0].LastReadinessObservedAt).IsNull();
            await Assert.That(due[2].StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Restricted);
            await Assert.That(due[2].LastReadinessObservedAt).IsEqualTo(Cutoff.AddSeconds(-1));
            await Assert.That(due[2].SupportedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
            await Assert.That(due[2].ExternalAccountId).IsEqualTo(stale.ExternalAccountId);
            await Assert.That(due[2].ConnectPlatformId).IsEqualTo(stale.ConnectPlatformId);
        }
    }

    [Test]
    [Arguments("active")]
    [Arguments("disabled")]
    [Arguments("replaced")]
    [Arguments("deleted")]
    public async Task NativeHistoricalOwnershipRetainsForeignTenantAndTerminalIdentity(string state)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        Scope first = await SeedScopeAsync(seed);
        Scope foreign = await SeedScopeAsync(seed);
        OrganizerPaymentProviderConnection local = Connection(first, "local", Now.AddDays(-2));
        OrganizerPaymentProviderConnection historical = Connection(foreign, "historical", Now.AddDays(-1));
        OrganizerPaymentProviderConnection? replacement = null;
        switch (state)
        {
            case "disabled":
                historical.Disable("operator_disabled", Now);
                break;
            case "replaced":
                replacement = historical.ReplaceWith(Guid.CreateVersion7(), $"acct_{Guid.CreateVersion7():N}", Now);
                seed.Add(replacement);
                break;
            case "deleted":
                historical.IsDeleted = true;
                historical.DeletedAt = Now;
                break;
        }
        seed.AddRange(local, historical);
        await seed.SaveChangesAsync();
        if (replacement is not null)
        {
            historical.MarkReplacedBy(replacement.Id);
            await seed.SaveChangesAsync();
        }
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        await Assert.That(await runtime.OrganizerPaymentProviderConnections.AnyAsync(row => row.Id == local.Id)).IsTrue();
        await Assert.That(await runtime.OrganizerPaymentProviderConnections.AnyAsync(row => row.Id == historical.Id)).IsFalse();
        var repository = new OrganizerPaymentProviderConnectionRepository(runtime);
        await Assert.That(await repository.GetHistoricalByExternalAccountAsync(
            "other-provider", historical.ConnectPlatformId, historical.ExternalAccountId, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetHistoricalByExternalAccountAsync(
            "stripe", "unconfigured-platform", historical.ExternalAccountId, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetHistoricalByExternalAccountAsync(
            "stripe", historical.ConnectPlatformId, $"acct_{Guid.CreateVersion7():N}", CancellationToken.None)).IsNull();

        OrganizerPaymentProviderConnection? owner = await repository.GetHistoricalByExternalAccountAsync(
            "stripe", historical.ConnectPlatformId, historical.ExternalAccountId, CancellationToken.None);

        await Assert.That(runtime.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(owner).IsNotNull();
        await Assert.That(owner!.Id).IsEqualTo(historical.Id);
        await Assert.That(owner.TenantId).IsEqualTo(foreign.TenantId);
        await Assert.That(owner.OrganizerActorId).IsEqualTo(foreign.ActorId);
        await Assert.That(owner.ConnectPlatformId).IsEqualTo(historical.ConnectPlatformId);
        await Assert.That(owner.ExternalAccountId).IsEqualTo(historical.ExternalAccountId);
        await Assert.That(owner.StatusId).IsEqualTo(historical.StatusId);
        await Assert.That(owner.IsDeleted).IsEqualTo(state == "deleted");
        await Assert.That(owner.ReplacedByConnectionId).IsEqualTo(replacement?.Id);
        await Assert.That(owner.ConcurrencyStamp).IsEqualTo(historical.ConcurrencyStamp);
    }

    [Test]
    public async Task NativeHistoricalWebhookOwnershipReturnsTwoMatchesInsteadOfFalseUniqueTenant()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        Scope first = await SeedScopeAsync(seed);
        Scope foreign = await SeedScopeAsync(seed);
        string account = $"acct_{Guid.CreateVersion7():N}";
        OrganizerPaymentProviderConnection older = Connection(first, "platform-one", Now.AddDays(-2), account);
        older.Disable("operator_disabled", Now);
        OrganizerPaymentProviderConnection newer = Connection(foreign, "platform-two", Now.AddDays(-1), account);
        newer.IsDeleted = true;
        newer.DeletedAt = Now;
        seed.AddRange(older, newer);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new OrganizerPaymentProviderConnectionRepository(runtime);
        await Assert.That((await repository.ListHistoricalByExternalAccountAsync(
            "other-provider", account, 2, CancellationToken.None)).Count).IsEqualTo(0);

        IReadOnlyList<OrganizerPaymentProviderConnection> matches =
            await repository.ListHistoricalByExternalAccountAsync("stripe", account, 2, CancellationToken.None);

        await Assert.That(runtime.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(matches.Count).IsEqualTo(2);
        await Assert.That(matches.Select(row => row.Id).SequenceEqual([older.Id, newer.Id])).IsTrue();
        await Assert.That(matches.Select(row => row.TenantId).SequenceEqual([first.TenantId, foreign.TenantId])).IsTrue();
        await Assert.That(matches.Select(row => row.OrganizerActorId).SequenceEqual([first.ActorId, foreign.ActorId])).IsTrue();
        await Assert.That(matches[0].StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Disabled);
        await Assert.That(matches[1].IsDeleted).IsTrue();
        await Assert.That(matches[0].ExternalAccountId).IsEqualTo(account);
        await Assert.That(matches[1].ExternalAccountId).IsEqualTo(account);
        await Assert.That(matches[0].ConnectPlatformId).IsEqualTo("platform-one");
        await Assert.That(matches[1].ConnectPlatformId).IsEqualTo("platform-two");
        IReadOnlyList<OrganizerPaymentProviderConnection> bounded =
            await repository.ListHistoricalByExternalAccountAsync("stripe", account, 1, CancellationToken.None);
        await Assert.That(bounded.Count).IsEqualTo(1);
        await Assert.That(bounded[0].Id).IsEqualTo(older.Id);
    }

    [Test]
    public async Task NativeReadinessApplicationUsesExactTenantAndPreservesForeignState()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        Scope first = await SeedScopeAsync(seed);
        Scope foreign = await SeedScopeAsync(seed);
        OrganizerPaymentProviderConnection local = Connection(first, "platform-one", Now.AddDays(-1));
        OrganizerPaymentProviderConnection other = Connection(foreign, "platform-two", Now.AddDays(-1));
        seed.AddRange(local, other);
        await seed.SaveChangesAsync();
        Guid foreignStamp = other.ConcurrencyStamp;
        await using var authority = await CreateAuthorityAsync(seed);
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId))
        {
            var repository = new OrganizerPaymentProviderConnectionRepository(runtime);
            await Assert.That(await repository.GetByTenantAndIdForUpdateAsync(foreign.TenantId, other.Id, CancellationToken.None)).IsNull();
            await Assert.That(await repository.GetByTenantAndIdForUpdateAsync(first.TenantId, other.Id, CancellationToken.None)).IsNull();
            await Assert.That(await repository.GetByTenantProviderAndExternalAccountForUpdateAsync(
                foreign.TenantId, "stripe", other.ExternalAccountId, CancellationToken.None)).IsNull();
            OrganizerPaymentProviderConnection? connection =
                await repository.GetByTenantAndIdForUpdateAsync(first.TenantId, local.Id, CancellationToken.None);
            await Assert.That(connection).IsNotNull();
            connection!.ApplyReadiness(Observation(Now, ready: true));
            await repository.SaveChangesAsync(CancellationToken.None);
        }
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId))
        {
            var repository = new OrganizerPaymentProviderConnectionRepository(runtime);
            OrganizerPaymentProviderConnection? ready = await repository.GetActiveByScopeAsync(
                first.TenantId, first.ActorId, "stripe", "platform-one", CancellationToken.None);
            await Assert.That(ready!.Id).IsEqualTo(local.Id);
            await Assert.That(ready.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Ready);
            await Assert.That(ready.LastReadinessObservedAt).IsEqualTo(Now);
            await Assert.That(ready.LastReadinessEvidenceRevision).IsEqualTo("native-readiness");
            await Assert.That(ready.MerchantCountryCode).IsEqualTo("BE");
            await Assert.That(ready.SupportedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
            var recipient = ready.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now);
            await Assert.That(recipient.TenantId).IsEqualTo(first.TenantId);
            await Assert.That(recipient.OrganizerActorId).IsEqualTo(first.ActorId);
            await Assert.That(recipient.ExternalAccountId).IsEqualTo(local.ExternalAccountId);
            await Assert.That(recipient.ConnectPlatformId).IsEqualTo("platform-one");
        }
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(foreign.TenantId))
        {
            OrganizerPaymentProviderConnection unchanged = await runtime.OrganizerPaymentProviderConnections.SingleAsync();
            await Assert.That(unchanged.Id).IsEqualTo(other.Id);
            await Assert.That(unchanged.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
            await Assert.That(unchanged.LastReadinessObservedAt).IsNull();
            await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(foreignStamp);
        }
    }

    [Test]
    public async Task NativeActiveScopeExcludesHistoricalDeletedAndForeignOwners()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        Scope first = await SeedScopeAsync(seed);
        Scope foreign = await SeedScopeAsync(seed);
        OrganizerPaymentProviderConnection disabled = Connection(first, "disabled", Now.AddDays(-1));
        disabled.Disable("operator_disabled", Now);
        OrganizerPaymentProviderConnection deleted = Connection(first, "deleted", Now.AddDays(-1));
        deleted.IsDeleted = true;
        deleted.DeletedAt = Now;
        OrganizerPaymentProviderConnection other = Connection(foreign, "foreign", Now.AddDays(-1));
        seed.AddRange(disabled, deleted, other);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new OrganizerPaymentProviderConnectionRepository(runtime);

        await Assert.That(await repository.GetActiveByScopeAsync(
            first.TenantId, first.ActorId, "stripe", "disabled", CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetActiveByScopeAsync(
            first.TenantId, first.ActorId, "stripe", "deleted", CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetActiveByScopeAsync(
            foreign.TenantId, foreign.ActorId, "stripe", "foreign", CancellationToken.None)).IsNull();
        await Assert.That((await repository.ListByTenantAndActorAsync(
            foreign.TenantId, foreign.ActorId, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That((await repository.ListByTenantAndActorAsync(
            first.TenantId, first.ActorId, CancellationToken.None)).Single().Id).IsEqualTo(disabled.Id);
    }

    private static OrganizerPaymentProviderReadinessObservation Observation(DateTime observedAt, bool ready) =>
        OrganizerPaymentProviderReadinessObservation.Create(
            "BE", ready ? ChargeCapabilityState.Active : ChargeCapabilityState.Pending,
            ready ? ProviderRequirementsState.Satisfied : ProviderRequirementsState.CurrentlyDue,
            ["EUR", "USD"], observedAt, "native-readiness");

    private static OrganizerPaymentProviderConnection Connection(
        Scope scope, string platform, DateTime createdAt, string? account = null) =>
        OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), scope.TenantId, scope.ActorId,
            "stripe", platform, account ?? $"acct_{Guid.CreateVersion7():N}", createdAt);

    private static async Task<Scope> SeedScopeAsync(ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            FullName = "Organizer payment authority",
            Slug = $"payment-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"payment-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Payment",
                LastName = "Organizer"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Payment organizer" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Add(actor);
        await context.SaveChangesAsync();
        return new(tenant.Id, actor.Id);
    }

    private async Task<PaymentAuthority> CreateAuthorityAsync(ExploreDbContext seed)
    {
        var connectionEntity = seed.Model.FindEntityType(typeof(OrganizerPaymentProviderConnection))!;
        var currencyEntity = seed.Model.FindEntityType(typeof(OrganizerPaymentProviderConnectionSupportedCurrency))!;
        string schema = connectionEntity.GetSchema()!;
        string connections = $"\"{schema}\".\"{connectionEntity.GetTableName()}\"";
        string currencies = $"\"{currencyEntity.GetSchema()}\".\"{currencyEntity.GetTableName()}\"";
        string role = $"organizer_payment_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var grant = administrator.CreateCommand();
        grant.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT, UPDATE ON {connections} TO "{role}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON {currencies} TO "{role}";
            """;
        await grant.ExecuteNonQueryAsync();
        await using var flags = administrator.CreateCommand();
        flags.CommandText = """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
            FROM pg_catalog.pg_roles WHERE rolname = @role
            """;
        flags.Parameters.AddWithValue("role", role);
        await Assert.That((bool)(await flags.ExecuteScalarAsync())!).IsTrue();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = role,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
        return new(fixture.ConnectionString, schema, connections, currencies, role, options);
    }

    private sealed record Scope(Guid TenantId, Guid ActorId);

    private sealed class PaymentAuthority(
        string administrationConnection, string schema, string connections, string currencies,
        string role, PrimaryDatabaseConnectionOptions options) : IAsyncDisposable
    {
        internal TenantExploreDbContextFactory Runtime { get; } = new(options, poolSize: 1);

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administrator = new NpgsqlConnection(administrationConnection);
            await administrator.OpenAsync();
            await using var revoke = administrator.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL PRIVILEGES ON {connections}, {currencies} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }
}
