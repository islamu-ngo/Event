using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.Announcements;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
public sealed class AnnouncementImportInstallationTests
{
    [Test]
    public async Task AnnouncementImportFirstInstallPreservesExistingForcedRlsAndRejectsLaterPolicyDrift()
    {
        // Roles are cluster-wide. A dedicated real fixture gives this test a first
        // installation without dropping roles or policies owned by other cases.
        await using var isolated = new PostgreSqlContainerFixture();
        await isolated.InitializeAsync();
        await using var migration = isolated.CreateDbContext();
        migration.BindTenantLease();
        var sql = new HumanPrincipalSourceSql(migration);
        string table = sql.Table(typeof(SystemSetting));
        await migration.Database.OpenConnectionAsync();
        await using (var absent = migration.Database.GetDbConnection().CreateCommand())
        {
            absent.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname=@owner)";
            absent.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Text)
            { Value = PostgresConfigurationImportAnnouncementContract.OwnerRole });
            await Assert.That((bool)(await absent.ExecuteScalarAsync())!).IsFalse();
        }
        await isolated.PrepareLocalIdentityWriterAsync();
        await using (var forced = migration.Database.GetDbConnection().CreateCommand())
        {
            forced.CommandText = """
                SELECT relrowsecurity AND relforcerowsecurity
                FROM pg_catalog.pg_class WHERE oid=@relation::regclass
                """;
            forced.Parameters.Add(new NpgsqlParameter("relation", NpgsqlDbType.Text) { Value = table });
            await Assert.That((bool)(await forced.ExecuteScalarAsync())!).IsTrue();
        }
        async Task<Dictionary<string, JsonNode>> ReadPolicies()
        {
            await using var command = migration.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT p.polname, pg_catalog.jsonb_build_object(
                    'oid',p.oid,'command',p.polcmd::text,'permissive',p.polpermissive,'roles',p.polroles,
                    'using',pg_catalog.pg_get_expr(p.polqual,p.polrelid),
                    'check',pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid))::text
                FROM pg_catalog.pg_policy p WHERE p.polrelid=@relation::regclass
                """;
            command.Parameters.Add(new NpgsqlParameter("relation", NpgsqlDbType.Text) { Value = table });
            var policies = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                policies.Add(reader.GetString(0), JsonNode.Parse(reader.GetString(1))
                    ?? throw new InvalidOperationException("A live row policy must have catalog metadata."));
            return policies;
        }
        var existingPolicies = await ReadPolicies();
        await Assert.That(existingPolicies.Count > 0).IsTrue();

        // The current defect fails here, before any announcement storage call.
        await PostgresConfigurationImportAnnouncementContract.ApplyAsync(migration, isolated.NativeQueueEnrollment);
        await PostgresConfigurationImportAnnouncementContract.ApplyAsync(migration, isolated.NativeQueueEnrollment);
        var installedPolicies = await ReadPolicies();
        foreach (var policy in existingPolicies)
        {
            await Assert.That(installedPolicies.ContainsKey(policy.Key)).IsTrue();
            await Assert.That(JsonNode.DeepEquals(installedPolicies[policy.Key], policy.Value)).IsTrue();
        }
        await Assert.That(installedPolicies.Count).IsEqualTo(existingPolicies.Count + 3);

        string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        var original = new AnnouncementGroup(Guid.CreateVersion7(), true,
            new(Guid.CreateVersion7(), new("Existing RLS publication", "", "")));
        migration.SystemSettings.Add(new SystemSetting
        {
            SettingKey = key, Value = AnnouncementSettingValueCodec.SerializeGroup(original),
            ValueType = SettingValueType.Json, IsLocked = false
        });
        await migration.SaveChangesAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            isolated.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using (var caller = new ExploreDbContext(options.Options))
        {
            caller.BindConfigurationInstanceImportOwner();
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var image = await PostgresConfigurationImportAnnouncementContract.ReadAsync(
                caller, isolated.NativeQueueEnrollment, null, [], CancellationToken.None);
            var before = image["System"]!.DeepClone().AsObject();
            var after = before.DeepClone().AsObject();
            after[nameof(SystemSetting.Value)] = AnnouncementSettingValueCodec.SerializeGroup(
                new(Guid.CreateVersion7(), false, original.Publication));
            after[nameof(SystemSetting.IsLocked)] = true;
            after[nameof(SystemSetting.UpdatedAt)] = before[nameof(SystemSetting.CreatedAt)]?.DeepClone();
            await PostgresConfigurationImportAnnouncementContract.WriteAsync(
                caller, isolated.NativeQueueEnrollment, null, before, after, CancellationToken.None);
            await transaction.CommitAsync();
        }
        var persisted = await migration.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        var group = AnnouncementSettingValueCodec.DeserializeGroup(persisted.Value)
            ?? throw new InvalidOperationException("The restricted Global role must preserve the complete group.");
        await Assert.That(group.Enabled).IsFalse();
        await Assert.That(group.Publication?.Edition).IsEqualTo(original.Publication?.Edition);
        await Assert.That(persisted.IsLocked).IsTrue();

        string readPolicy;
        await using (var query = migration.Database.GetDbConnection().CreateCommand())
        {
            query.CommandText = """
                SELECT polname FROM pg_catalog.pg_policy
                WHERE polrelid=@relation::regclass AND polcmd='r'
                    AND polroles=ARRAY[pg_catalog.to_regrole(@owner)::oid]
                """;
            query.Parameters.Add(new NpgsqlParameter("relation", NpgsqlDbType.Text) { Value = table });
            query.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Text)
            { Value = PostgresConfigurationImportAnnouncementContract.OwnerRole });
            readPolicy = (string)(await query.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("The fixed import owner must have its own read policy."));
        }
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync(
                $"ALTER POLICY {sql.Quote(readPolicy)} ON {table} USING (false)");
            var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresConfigurationImportAnnouncementContract.ApplyAsync(migration, isolated.NativeQueueEnrollment));
            await Assert.That(refusal?.SqlState).IsEqualTo("55000");
            await transaction.RollbackAsync();
        }
    }

}

public sealed partial class OrdinarySettingsNativeContractTests
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Global, SettingScope.Instance)]
    [Arguments(PrimaryDatabaseRole.Runtime, SettingScope.Tenant)]
    [Arguments(PrimaryDatabaseRole.Runtime, SettingScope.User)]
    public async Task AnnouncementNoWriteCannotCompleteAnUnrelatedAcceptedOrdinaryEffect(
        PrimaryDatabaseRole role, SettingScope scope)
    {
        await InstallAsync();
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? Guid.CreateVersion7() : null;
        Guid? user = scope == SettingScope.User ? Guid.CreateVersion7() : null;
        await using var migration = fixture.CreateDbContext();
        if (tenant is { } selectedTenant)
            migration.TenantContext = new SelectedTenant(selectedTenant);
        migration.BindTenantLease();
        if (tenant is { } tenantId)
        {
            migration.Tenants.Add(Tenant(tenantId));
            await migration.SaveChangesAsync();
        }
        await using var caller = Open(role, tenant);
        await caller.Database.OpenConnectionAsync();
        string requestedValue = JsonSerializer.Serialize($"ordinary-no-write-{Guid.CreateVersion7():N}");
        var request = Request(scope, tenant, user, Key, requestedValue);
        var before = await ReadAsync(caller, tenant, user, request["Keys"]);
        string group = scope == SettingScope.Instance ? "System" : scope == SettingScope.Tenant ? "Tenant" : "User";
        string? oldValue = before["Image"]![group]![Key]?["Value"]?.GetValue<string>();
        await Assert.That(oldValue).IsNotEqualTo(requestedValue);
        bool reachedNormalCompletion = false;
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            // Acquisition must succeed outside the refusal assertion: this is an admitted,
            // non-disclosure write on the actual service role, not a wrong-role denial.
            await AcquireAsync(caller, before, request, tenant, user);
            var expected = before["Image"]!.DeepClone();
            var refusal = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await ExecuteAsync(caller, "unchanged",
                [
                    new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = Key },
                    new NpgsqlParameter("expected", NpgsqlDbType.Jsonb) { Value = expected.ToJsonString() }
                ]);
                reachedNormalCompletion = true;
                await CheckAsync(caller, "complete", expected);
            });
            await Assert.That(refusal?.SqlState).IsEqualTo("42501");
            await Assert.That(reachedNormalCompletion).IsFalse();
            await transaction.RollbackAsync();
        }
        string? persisted = scope == SettingScope.Instance
            ? await migration.SystemSettings.AsNoTracking().Where(row => row.SettingKey == Key)
                .Select(row => row.Value).SingleOrDefaultAsync()
            : scope == SettingScope.Tenant
                ? await migration.Set<TenantSetting>().AsNoTracking()
                    .Where(row => row.TenantId == tenant && row.SettingKey == Key)
                    .Select(row => row.Value).SingleOrDefaultAsync()
                : await migration.UserPreferences.AsNoTracking()
                    .Where(row => row.TenantId == tenant && row.UserId == user && row.SettingKey == Key)
                    .Select(row => row.Value).SingleOrDefaultAsync();
        await Assert.That(persisted).IsEqualTo(oldValue);
    }

    [Test]
    public async Task AnnouncementImportStorageWritesTheInstanceGroup()
    {
        await InstallAsync();
        string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        await using var migration = fixture.CreateDbContext();
        migration.BindTenantLease();
        await PostgresConfigurationImportAnnouncementContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var previous = await migration.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        var original = new AnnouncementGroup(Guid.CreateVersion7(), true, new(Guid.CreateVersion7(), new("Instance", "", "")));
        var row = await migration.SystemSettings.SingleOrDefaultAsync(value => value.SettingKey == key);
        if (row is null)
        {
            row = new SystemSetting
            {
                SettingKey = key, ValueType = SettingValueType.Json,
                Value = AnnouncementSettingValueCodec.SerializeGroup(original)
            };
            migration.SystemSettings.Add(row);
        }
        row.Value = AnnouncementSettingValueCodec.SerializeGroup(original);
        row.IsLocked = true;
        await migration.SaveChangesAsync();
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            caller.BindConfigurationInstanceImportOwner();
            await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var image = await PostgresConfigurationImportAnnouncementContract.ReadAsync(
                    caller, fixture.NativeQueueEnrollment, null, [], CancellationToken.None);
                var before = image["System"]!.DeepClone().AsObject();
                var after = before.DeepClone().AsObject();
                after[nameof(SystemSetting.Value)] = AnnouncementSettingValueCodec.SerializeGroup(
                    new(Guid.CreateVersion7(), false, original.Publication));
                after[nameof(SystemSetting.IsLocked)] = false;
                after[nameof(SystemSetting.UpdatedAt)] = before[nameof(SystemSetting.CreatedAt)]?.DeepClone();
                await PostgresConfigurationImportAnnouncementContract.WriteAsync(
                    caller, fixture.NativeQueueEnrollment, null, before, after, CancellationToken.None);
                await transaction.CommitAsync();
            }
            var persisted = await migration.SystemSettings.AsNoTracking().SingleAsync(value => value.SettingKey == key);
            var group = AnnouncementSettingValueCodec.DeserializeGroup(persisted.Value)
                ?? throw new InvalidOperationException("The instance group must remain complete.");
            await Assert.That(group.Enabled).IsFalse();
            await Assert.That(group.Publication?.Edition).IsEqualTo(original.Publication?.Edition);
            await Assert.That(persisted.IsLocked).IsFalse();
        }
        finally
        {
            migration.ChangeTracker.Clear();
            if (previous is null)
                await migration.SystemSettings.Where(value => value.SettingKey == key).ExecuteDeleteAsync();
            else
                await migration.SystemSettings.Where(value => value.SettingKey == key).ExecuteUpdateAsync(update =>
                    update.SetProperty(value => value.Value, previous.Value)
                        .SetProperty(value => value.IsLocked, previous.IsLocked)
                        .SetProperty(value => value.UpdatedAt, previous.UpdatedAt)
                        .SetProperty(value => value.UpdatedBy, previous.UpdatedBy));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnnouncementImportStorageRequiresItsExactOriginalRow(bool substituteBefore)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), actor = Guid.CreateVersion7();
        string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        var original = new AnnouncementGroup(Guid.CreateVersion7(), true, new(Guid.CreateVersion7(), new("Original", "", "")));
        string originalValue = AnnouncementSettingValueCodec.SerializeGroup(original);
        await using var migration = fixture.CreateDbContext();
        migration.TenantContext = new SelectedTenant(tenant);
        migration.BindTenantLease();
        await PostgresConfigurationImportAnnouncementContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        migration.Tenants.Add(Tenant(tenant));
        migration.Set<TenantSetting>().Add(new TenantSetting
        {
            TenantId = tenant, Tenant = null!, SettingKey = key, Value = originalValue, IsLocked = true
        });
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        caller.BindConfigurationInstanceImportOwner();
        var replacement = new AnnouncementGroup(Guid.CreateVersion7(), false,
            new(Guid.CreateVersion7(), new("Replacement", "", "")));
        string replacementValue = AnnouncementSettingValueCodec.SerializeGroup(replacement);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var image = await PostgresConfigurationImportAnnouncementContract.ReadAsync(
                caller, fixture.NativeQueueEnrollment, null, [tenant], CancellationToken.None);
            var before = image["Tenants"]!.AsArray().Single()!.DeepClone().AsObject();
            var after = before.DeepClone().AsObject();
            after[nameof(TenantSetting.Value)] = replacementValue;
            after[nameof(TenantSetting.UpdatedAt)] = before[nameof(TenantSetting.CreatedAt)]?.DeepClone();
            after[nameof(TenantSetting.UpdatedBy)] = actor.ToString("D");
            if (substituteBefore)
                before[nameof(TenantSetting.Value)] = "substituted";
            Task Write() => PostgresConfigurationImportAnnouncementContract.WriteAsync(
                caller, fixture.NativeQueueEnrollment, tenant, before, after, CancellationToken.None);
            if (substituteBefore)
            {
                await Assert.ThrowsAsync<PostgresException>(Write);
                await transaction.RollbackAsync();
            }
            else
            {
                await Write();
                await transaction.CommitAsync();
            }
        }
        var stored = await migration.Set<TenantSetting>().AsNoTracking().SingleAsync(row => row.TenantId == tenant && row.SettingKey == key);
        await Assert.That(stored.Value).IsEqualTo(substituteBefore ? originalValue : replacementValue);
        await Assert.That(stored.IsLocked).IsTrue();
    }

    [Test]
    public async Task AnnouncementSourceImageContainsBothOwnerGroupsAndExplicitMarkerAbsence()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.TenantContext = new SelectedTenant(tenant);
        migration.BindTenantLease();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        await caller.Database.OpenConnectionAsync();
        string publication = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        string marker = GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition;
        var image = await ReadAsync(caller, tenant, user, new JsonArray(publication, marker));

        await Assert.That(image["Image"]!["System"]!.AsObject().ContainsKey(publication)).IsTrue();
        await Assert.That(image["Image"]!["Tenant"]!.AsObject().ContainsKey(publication)).IsTrue();
        await Assert.That(image["Image"]!["Tenant"]![publication]).IsNull();
        await Assert.That(image["Image"]!["User"]!.AsObject().ContainsKey(marker)).IsTrue();
        await Assert.That(image["Image"]!["User"]![marker]).IsNull();
    }

    [Test]
    public async Task MarkerInsertedAfterObservationCannotBeOverwrittenFromAnAbsentPreimage()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.TenantContext = new SelectedTenant(tenant);
        migration.BindTenantLease();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        await caller.Database.OpenConnectionAsync();
        string key = GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition;
        var marker = new AnnouncementDismissalMarker(new(tenant, AnnouncementOwner.Instance,
            Guid.CreateVersion7(), new(Guid.CreateVersion7(), false, null, false)));
        string stored = AnnouncementSettingValueCodec.SerializeMarker(marker);
        var request = Request(SettingScope.User, tenant, user, key, stored);
        request["Keys"] = new JsonArray(GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication, key);
        var before = await ReadAsync(caller, tenant, user, request["Keys"]);
        migration.UserPreferences.Add(new UserPreference
        {
            TenantId = tenant, Tenant = null!, UserId = user, SettingKey = key, Value = stored
        });
        await migration.SaveChangesAsync();

        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<PostgresException>(() => AcquireAsync(caller, before, request, tenant, user));
            await transaction.RollbackAsync();
        }
        await Assert.That((await migration.UserPreferences.AsNoTracking().SingleAsync(row =>
            row.TenantId == tenant && row.UserId == user && row.SettingKey == key)).Value).IsEqualTo(stored);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NoWriteCompletionRequiresTheExactExcludedImageAndLeavesMarkerAbsent(bool substituteImage)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.TenantContext = new SelectedTenant(tenant);
        migration.BindTenantLease();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        await caller.Database.OpenConnectionAsync();
        string key = GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition;
        var marker = new AnnouncementDismissalMarker(new(tenant, AnnouncementOwner.Instance,
            Guid.CreateVersion7(), new(Guid.CreateVersion7(), false, null, false)));
        var request = Request(SettingScope.User, tenant, user, key,
            AnnouncementSettingValueCodec.SerializeMarker(marker));
        request["Keys"] = new JsonArray(GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication, key);
        var before = await ReadAsync(caller, tenant, user, request["Keys"]);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before, request, tenant, user);
        var expected = before["Image"]!.DeepClone();
        if (substituteImage)
            expected["User"]![key] = new JsonObject { ["Value"] = "substituted" };
        Task CompleteWithoutWrite() => ExecuteAsync(caller, "unchanged",
        [
            new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = key },
            new NpgsqlParameter("expected", NpgsqlDbType.Jsonb) { Value = expected.ToJsonString() }
        ]);
        if (substituteImage)
        {
            await Assert.ThrowsAsync<PostgresException>(CompleteWithoutWrite);
            await transaction.RollbackAsync();
        }
        else
        {
            await CompleteWithoutWrite();
            await CheckAsync(caller, "complete", expected);
            await transaction.CommitAsync();
        }
        await Assert.That(await migration.UserPreferences.AnyAsync(row =>
            row.TenantId == tenant && row.UserId == user && row.SettingKey == key)).IsFalse();
    }
}
