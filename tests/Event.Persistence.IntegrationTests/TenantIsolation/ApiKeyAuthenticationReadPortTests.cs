using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ApiKeyAuthenticationReadPortTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactLookupAndUsageDoNotBorrowTheRequestTenant(bool foreignTenant)
    {
        Guid ownerTenant = Guid.CreateVersion7();
        Guid requestTenant = Guid.CreateVersion7();
        var key = Key(ownerTenant);
        var other = Key(requestTenant);
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var human = new HumanSecuritySourceGate(migrator);
        await using var humanLease = await human.AcquireAsync(default);
        await using var enrollmentGate = new EventDiscoveryEnrollmentGate(migrator);
        await using var enrollmentLease = await enrollmentGate.AcquireAsync(default);
        migrator.Tenants.AddRange(Tenant(ownerTenant), Tenant(requestTenant));
        migrator.ExternalApiKeys.AddRange(key, other);
        await migrator.SaveChangesAsync();
        await using var request = Open(PrimaryDatabaseRole.Runtime);
        if (foreignTenant) request.TenantContext = new FixedTenant(requestTenant);
        request.BindTenantLease();
        await request.Database.OpenConnectionAsync();
        var entity = request.Model.FindEntityType(typeof(ExternalApiKey))!;
        string table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        await using (var direct = request.Database.GetDbConnection().CreateCommand())
        {
            direct.CommandText = $"SELECT * FROM {table}";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await direct.ExecuteScalarAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("42501");
        }
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new ExternalApiKeyAuthenticationRepository(factory, fixture.NativeQueueEnrollment);

        var found = await repository.GetByKeyIdForAuthentication(key.KeyId);

        await Assert.That(found!.Id).IsEqualTo(key.Id);
        await Assert.That(found.SecretHash == key.SecretHash).IsTrue();
        await Assert.That(await repository.GetByKeyIdForAuthentication("absent-key")).IsNull();
        DateTime now = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        await Assert.That(await repository.TouchUsageMetadata(key.Id, now, "192.0.2.1", TimeSpan.FromMinutes(5))).IsTrue();
        await Assert.That(await repository.TouchUsageMetadata(key.Id, now.AddMinutes(5), "192.0.2.2", TimeSpan.FromMinutes(5))).IsFalse();
        await Assert.That(await repository.TouchUsageMetadata(key.Id, now.AddMinutes(5).AddSeconds(1), "192.0.2.3", TimeSpan.FromMinutes(5))).IsTrue();
        migrator.ChangeTracker.Clear();
        var actual = await migrator.ExternalApiKeys.IgnoreQueryFilters().SingleAsync(row => row.Id == key.Id);
        await Assert.That(actual.LastUsedIp).IsEqualTo("192.0.2.3");
        await Assert.That(actual.SecretHash == key.SecretHash).IsTrue();
        await Assert.That(actual.Scopes).IsEqualTo(key.Scopes);
        await Assert.That(actual.ExternalApiKeyStatusId).IsEqualTo(key.ExternalApiKeyStatusId);
        await Assert.That(await migrator.ExternalApiKeys.IgnoreQueryFilters().Where(row => row.Id == other.Id)
            .Select(row => row.LastUsedAt).SingleAsync()).IsNull();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? requestTenant : (Guid?)null);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UndeclaredLookupOrUsageExecutorFailsClosed(bool usage)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        var entity = migrator.Model.FindEntityType(typeof(ExternalApiKey))!;
        string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
        string permission = usage ? "UPDATE" : "SELECT";
        string column = Quote(entity.FindProperty(usage ? nameof(ExternalApiKey.LastUsedIp) : nameof(ExternalApiKey.SecretHash))!.GetColumnName()!);
        string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        await using var global = await GlobalAsync();
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await migrator.Database.ExecuteSqlRawAsync($"GRANT {permission} ({column}) ON {table} TO {runtime}");
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            // Applying the fixed profile must not repair the unauthorized source grant.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE {permission} ({column}) ON {table} FROM {runtime}");
        }
        await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RuntimeCannotInheritLookupOrUsageAuthority(bool usage, bool throughGlobalLogin)
    {
        await using var context = Open(PrimaryDatabaseRole.Migrator);
        var enrollment = fixture.NativeQueueEnrollment;
        string borrowed = throughGlobalLogin ? enrollment.GlobalLogin : PostgresExternalApiKeyAuthenticationContract.GlobalRole;
        await using var global = await GlobalAsync();
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, enrollment);
        await context.Database.ExecuteSqlRawAsync(
            $"GRANT {Quote(borrowed)} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                if (usage) await ExternalApiKeyAuthenticationQuery.TouchUsageAsync(global, Guid.CreateVersion7(),
                    DateTime.UtcNow, null, DateTime.UtcNow.AddMinutes(-5), default, enrollment);
                else await ExternalApiKeyAuthenticationQuery.ReadAsync(global, "absent-key", default, enrollment);
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(context, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, enrollment));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync($"REVOKE {Quote(borrowed)} FROM {Quote(enrollment.RuntimeLogin)}");
        }
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(global, enrollment);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticationUsageTouchOwnsOnlyItsExactKeyAndTwoMetadataFields(bool platformKey)
    {
        Guid ownerTenant = Guid.CreateVersion7();
        Guid otherTenant = Guid.CreateVersion7();
        var tenantKey = Key(ownerTenant);
        var other = Key(otherTenant);
        var platform = Key(null);
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var human = new HumanSecuritySourceGate(migrator);
        await using var humanLease = await human.AcquireAsync(default);
        await using var enrollmentGate = new EventDiscoveryEnrollmentGate(migrator);
        await using var enrollmentLease = await enrollmentGate.AcquireAsync(default);
        migrator.Tenants.AddRange(Tenant(ownerTenant), Tenant(otherTenant));
        migrator.ExternalApiKeys.AddRange(tenantKey, other, platform);
        await migrator.SaveChangesAsync();
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new ExternalApiKeyAuthenticationRepository(factory, fixture.NativeQueueEnrollment);
        ExternalApiKey key = platformKey ? platform : tenantKey;
        DateTime first = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan interval = TimeSpan.FromMinutes(5);

        await Assert.That(await repository.TouchUsageMetadata(key.Id, first, "192.0.2.1", interval)).IsTrue();
        await Assert.That(await repository.TouchUsageMetadata(
            key.Id, first.AddMinutes(5), "192.0.2.2", interval)).IsFalse();
        await Assert.That(await repository.TouchUsageMetadata(
            key.Id, first.AddMinutes(5).AddSeconds(1), "192.0.2.3", interval)).IsTrue();

        migrator.ChangeTracker.Clear();
        ExternalApiKey actual = await migrator.ExternalApiKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == key.Id);
        await Assert.That(actual.LastUsedAt).IsEqualTo(first.AddMinutes(5).AddSeconds(1));
        await Assert.That(actual.LastUsedIp).IsEqualTo("192.0.2.3");
        await Assert.That(actual.SecretHash == key.SecretHash).IsTrue();
        await Assert.That(actual.ExternalApiKeyStatusId).IsEqualTo(key.ExternalApiKeyStatusId);
        await Assert.That(actual.Scopes).IsEqualTo(key.Scopes);
        await Assert.That(actual.OwnerId).IsEqualTo(key.OwnerId);
        await Assert.That(actual.OwnerType).IsEqualTo(key.OwnerType);
        await Assert.That(actual.TenantId).IsEqualTo(key.TenantId);
        await Assert.That(actual.UpdatedAt).IsEqualTo(key.UpdatedAt);
        await Assert.That(await migrator.ExternalApiKeys.IgnoreQueryFilters().Where(row => row.Id == other.Id)
            .Select(row => row.LastUsedAt).SingleAsync()).IsNull();
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options);
        if (role == PrimaryDatabaseRole.Migrator) context.BindTenantLease();
        return context;
    }

    private async Task<ExploreDbContext> GlobalAsync()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var context = await factory.CreateDbContextAsync(default);
        await context.Database.OpenConnectionAsync();
        return context;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = $"Authentication {id:N}",
        Slug = $"auth-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static ExternalApiKey Key(Guid? tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Name = "Authentication key",
        KeyId = ApiKeyHashing.CreateKeyId(),
        SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
        Scopes = "events:read",
        OwnerType = tenantId.HasValue ? ExternalApiKeyOwnerType.Tenant : ExternalApiKeyOwnerType.InstanceAdmin,
        OwnerId = tenantId ?? Guid.CreateVersion7(),
        ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.PendingRotation,
        ExternalApiKeyStatus = null!,
        ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
        ExternalApiKeyCreditPeriod = null!
    };

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
}
