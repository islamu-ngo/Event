using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

/// <summary>Exercises trusted service SQL, not fabricated managed human authentication.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class OrdinarySettingsNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    public async Task OtherServicePurposesCannotObserveOrdinarySources(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller));
    }

    [Test]
    public async Task GlobalReadsWholeSystemCorpusAndExplicitAbsencesWithoutTableWrites()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject observed = await ReadAsync(caller);
        await using var migration = fixture.CreateDbContext();
        var originals = await migration.SystemSettings.AsNoTracking().ToListAsync();
        foreach (var row in originals)
        {
            JsonNode image = observed["Image"]!["System"]![row.SettingKey]!;
            await Assert.That(image["Id"]!.GetValue<Guid>()).IsEqualTo(row.Id);
            await Assert.That(image["Description"]?.GetValue<string>()).IsEqualTo(row.Description);
        }
        await Assert.That(observed["Image"]!["System"]!.AsObject().ContainsKey(MissingKey)).IsTrue();
        await Assert.That(observed["Image"]!["System"]![MissingKey]).IsNull();
        await Assert.ThrowsAsync<PostgresException>(async () => await caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {new HumanPrincipalSourceSql(caller).Table(typeof(SystemSetting))} SET value = value WHERE false"));
    }

    [Test]
    public async Task MissingPrivateCompletionCannotSettleEvenWithoutAnEffect()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject before = await ReadAsync(caller);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before);
        await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
    }

    [Test]
    public async Task HierarchyReadPreservesOriginalTransactionAndDetachedSystemEntities()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var connection = caller.Database.GetDbConnection();
        var image = await PostgresOrdinarySettingsContract.ReadHierarchyAsync(
            caller, fixture.NativeQueueEnrollment, null, [MissingKey], CancellationToken.None);
        await using var migration = fixture.CreateDbContext();
        var originals = await migration.SystemSettings.AsNoTracking().ToListAsync();
        await Assert.That(image.System.Count).IsEqualTo(originals.Count);
        foreach (var original in originals)
        {
            var row = image.System.Single(value => value.SettingKey == original.SettingKey);
            await Assert.That(row.Id).IsEqualTo(original.Id);
            await Assert.That(row.Value).IsEqualTo(original.Value);
            await Assert.That(row.IsLocked).IsEqualTo(original.IsLocked);
            await Assert.That(row.SettingValueTypeId).IsEqualTo(original.SettingValueTypeId);
            await Assert.That(row.AllowedValues).IsEqualTo(original.AllowedValues);
        }
        await Assert.That(image.Tenant.Count).IsEqualTo(0);
        await Assert.That(caller.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(ReferenceEquals(caller.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(ReferenceEquals(caller.Database.GetDbConnection(), connection)).IsTrue();
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await transaction.CommitAsync();
    }

    [Test]
    public async Task HierarchyReadCannotRetargetItsSelectedInstanceOwner()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresOrdinarySettingsContract.ReadHierarchyAsync(caller, fixture.NativeQueueEnrollment,
                Guid.CreateVersion7(), [Key], CancellationToken.None));
    }

    [Test]
    public async Task HierarchyRuntimeReadReturnsOnlySelectedTenantEntities()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), foreignTenant = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(tenant), Tenant(foreignTenant));
        var own = new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Tenant = null!,
            SettingKey = Key,
            Value = "\"selected\"",
            IsLocked = true,
            CreatedAt = Instant
        };
        migration.Set<TenantSetting>().AddRange(own, new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreignTenant,
            Tenant = null!,
            SettingKey = Key,
            Value = "\"foreign\"",
            CreatedAt = Instant
        });
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        caller.BindTenantLease();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var image = await PostgresOrdinarySettingsContract.ReadHierarchyAsync(
            caller, fixture.NativeQueueEnrollment, tenant, [Key, MissingKey], CancellationToken.None);
        await Assert.That(image.Tenant.Count).IsEqualTo(1);
        var row = image.Tenant.Single();
        await Assert.That(row.Id).IsEqualTo(own.Id);
        await Assert.That(row.TenantId).IsEqualTo(tenant);
        await Assert.That(row.Value).IsEqualTo("\"selected\"");
        await Assert.That(row.IsLocked).IsTrue();
        await Assert.That(row.CreatedAt).IsEqualTo(Instant);
        await Assert.That(caller.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresOrdinarySettingsContract.ReadHierarchyAsync(caller, fixture.NativeQueueEnrollment,
                foreignTenant, [Key], CancellationToken.None));
        await transaction.CommitAsync();
    }

    [Test]
    public async Task CallerCannotSubstituteAChangedExpectedImageForNativeCompletion()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject before = await ReadAsync(caller);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before);
        JsonNode expected = before["Image"]!.DeepClone();
        expected["System"]![MissingKey] = new JsonObject { ["Value"] = "substituted" };
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(caller, "complete",
            [new("expected", NpgsqlDbType.Jsonb) { Value = expected.ToJsonString() }]));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task OmittedEqualScalarEffectCannotBorrowOriginalImageAsCompletion()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        if (before["Image"]!["System"]![Key] is { } original)
            request["Effects"]![0]!["Value"] = original["Value"]!.DeepClone();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before, request);
        await CheckAsync(caller, "verify", before["Image"]!);
        await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(caller, "complete", before["Image"]!));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactSystemEffectCommitsOnlyItsCanonicalPreimageAndPostimage(bool forceInsert)
    {
        await InstallAsync();
        if (forceInsert)
        {
            await using var migration = fixture.CreateDbContext();
            await migration.SystemSettings.Where(row => row.SettingKey == Key).ExecuteDeleteAsync();
        }
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject afterRow = SystemAfter(before, request);
        JsonNode expected = before["Image"]!.DeepClone();
        expected["System"]![Key] = afterRow.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request);
            await CheckAsync(caller, "verify", before["Image"]!);
            await WriteAsync(caller, "System", 0, before["Image"]!["System"]![Key], afterRow);
            await CheckAsync(caller, "complete", expected);
            await transaction.CommitAsync();
        }
        var persisted = await ReadAsync(caller, keys: request["Keys"]);
        await Assert.That(JsonNode.DeepEquals(expected, persisted["Image"])).IsTrue();
        await using var observer = fixture.CreateDbContext();
        SystemSetting row = await observer.SystemSettings.AsNoTracking().SingleAsync(value => value.SettingKey == Key);
        await Assert.That(row.Value).IsEqualTo("\"native-contract\"");
        await Assert.That(row.Id).IsEqualTo(afterRow["Id"]!.GetValue<Guid>());
        await Assert.That(row.Description).IsEqualTo(afterRow["Description"]?.GetValue<string>());
        await Assert.That(row.CreatedBy).IsEqualTo(afterRow["CreatedBy"]?.GetValue<Guid>());
    }

    [Test]
    [Arguments("Id")]
    [Arguments("CreatedAt")]
    [Arguments("CreatedBy")]
    [Arguments("Description")]
    [Arguments("Type")]
    [Arguments("Locked")]
    [Arguments("Value")]
    public async Task CallerCannotWidenCanonicalSystemMetadataOrValue(string field)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject after = SystemAfter(before, request);
        after[field] = field switch
        {
            "Id" or "CreatedBy" => JsonValue.Create(Guid.CreateVersion7()),
            "Type" => JsonValue.Create(after["Type"]!.GetValue<int>() + 1),
            "Locked" => JsonValue.Create(!(after["Locked"]!.GetValue<bool>())),
            "CreatedAt" => JsonValue.Create(after["CreatedAt"]!.GetValue<string>() == "2000-01-01T00:00:00.0000000Z"
                ? "2001-01-01T00:00:00.0000000Z" : "2000-01-01T00:00:00.0000000Z"),
            "Description" => JsonValue.Create((after["Description"]?.GetValue<string>() ?? "") + " foreign-native-value"),
            _ => JsonValue.Create("foreign-native-value")
        };
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before, request);
        await Assert.ThrowsAsync<PostgresException>(() =>
            WriteAsync(caller, "System", 0, before["Image"]!["System"]![Key], after));
        await transaction.RollbackAsync();
        var persisted = await ReadAsync(caller, keys: request["Keys"]);
        await Assert.That(JsonNode.DeepEquals(before["Image"], persisted["Image"])).IsTrue();
    }

    [Test]
    public async Task DuplicateCanonicalWriteCannotObtainSecondPrivateCompletion()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject after = SystemAfter(before, request);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before, request);
        await WriteAsync(caller, "System", 0, before["Image"]!["System"]![Key], after);
        await Assert.ThrowsAsync<PostgresException>(() => WriteAsync(caller, "System", 0, after, after));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CompletedWriteWithoutTerminalCompletionStillRollsBackAtCommit()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request);
            await WriteAsync(caller, "System", 0, before["Image"]!["System"]![Key], SystemAfter(before, request));
            await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        }
        var persisted = await ReadAsync(caller, keys: request["Keys"]);
        await Assert.That(JsonNode.DeepEquals(before["Image"], persisted["Image"])).IsTrue();
    }

    [Test]
    public async Task RawEqualScalarUpdateCannotReplaceTheNativeWriter()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance);
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before, request);
        await Assert.ThrowsAsync<PostgresException>(async () => await caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {new HumanPrincipalSourceSql(caller).Table(typeof(SystemSetting))} SET value = value WHERE setting_key = {{0}}", Key));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("System", "ROW EXCLUSIVE")]
    [Arguments("Tenant", "ROW EXCLUSIVE")]
    [Arguments("User", "ROW EXCLUSIVE")]
    [Arguments("System", "ACCESS EXCLUSIVE")]
    [Arguments("Tenant", "ACCESS EXCLUSIVE")]
    [Arguments("User", "ACCESS EXCLUSIVE")]
    public async Task OriginalOwnerExcludesDmlAndTruncateLockModesUntilSettlement(string group, string mode)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject before = await ReadAsync(caller);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireAsync(caller, before);
        await using var contender = fixture.CreateDbContext();
        Type type = group == "System" ? typeof(SystemSetting) : group == "Tenant" ? typeof(TenantSetting) : typeof(UserPreference);
        string command = $"LOCK TABLE {new HumanPrincipalSourceSql(contender).Table(type)} IN {mode} MODE NOWAIT";
        await using (var blocked = await contender.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await contender.Database.ExecuteSqlRawAsync(command))
                ?? throw new InvalidOperationException("The original table fence must reject a competing writer lock.");
            await Assert.That(error.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await blocked.RollbackAsync();
        }
        await owner.RollbackAsync();
        await using var released = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlRawAsync(command);
        await released.RollbackAsync();
    }

    [Test]
    public async Task EarlierWriterDoesNotInvertPolicyFirstAcquisitionIntoATableWait()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject before = await ReadAsync(caller);
        await using var contender = fixture.CreateDbContext();
        await using var writer = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlRawAsync(
            $"LOCK TABLE {new HumanPrincipalSourceSql(contender).Table(typeof(SystemSetting))} IN ROW EXCLUSIVE MODE");
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var error = await Assert.ThrowsAsync<PostgresException>(() => AcquireAsync(caller, before))
            ?? throw new InvalidOperationException("The original acquisition must not wait behind a table writer.");
        await Assert.That(error.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await owner.RollbackAsync();
        await writer.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedNegativeSourceCannotBecomeAFreshSerializableBaseline(bool establishSnapshot)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string absent = $"ordinary-native-{Guid.CreateVersion7():N}";
        JsonObject before = await ReadAsync(caller, keys: new JsonArray(absent));
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (establishSnapshot) await caller.Database.ExecuteSqlRawAsync("SELECT pg_catalog.txid_current_snapshot()");
        await using var migration = fixture.CreateDbContext();
        var inserted = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = absent,
            Value = "false",
            SettingValueTypeId = (int)SettingValueType.Boolean,
            CreatedAt = Instant,
            IsLocked = false
        };
        migration.SystemSettings.Add(inserted);
        await migration.SaveChangesAsync();
        try
        {
            var request = EmptyRequest();
            request["Keys"] = new JsonArray(absent);
            await Assert.ThrowsAsync<PostgresException>(() => AcquireAsync(caller, before, request));
        }
        finally
        {
            await transaction.RollbackAsync();
            await migration.SystemSettings.Where(row => row.Id == inserted.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    [Arguments(SettingScope.Tenant)]
    [Arguments(SettingScope.User)]
    public async Task OriginalRuntimeReadsAndWritesOnlyItsExactTenantAndUser(SettingScope scope)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), otherTenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(tenant), Tenant(otherTenant));
        migration.Set<TenantSetting>().AddRange(
            new TenantSetting { TenantId = tenant, Tenant = null!, SettingKey = "ordinary-own-extra", Value = "own" },
            new TenantSetting { TenantId = otherTenant, Tenant = null!, SettingKey = "ordinary-foreign-extra", Value = "foreign" });
        migration.UserPreferences.AddRange(
            new UserPreference { TenantId = tenant, Tenant = null!, UserId = user, SettingKey = "ordinary-own-preference", Value = "own" },
            new UserPreference { TenantId = tenant, Tenant = null!, UserId = Guid.CreateVersion7(), SettingKey = "ordinary-other-user", Value = "foreign" },
            new UserPreference { TenantId = otherTenant, Tenant = null!, UserId = user, SettingKey = "ordinary-other-tenant", Value = "foreign" });
        await migration.SaveChangesAsync();
        await migration.SystemSettings.Where(row => row.SettingKey == Key)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsLocked, false));
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        await caller.Database.OpenConnectionAsync();
        Guid? selectedUser = scope == SettingScope.User ? user : null;
        JsonObject request = Request(scope, tenant, selectedUser);
        JsonObject before = await ReadAsync(caller, tenant, selectedUser, request["Keys"]);
        await Assert.That(before["Image"]!["Tenant"]!["ordinary-own-extra"]!["Value"]!.GetValue<string>()).IsEqualTo("own");
        await Assert.That(before["Image"]!["Tenant"]!.AsObject().ContainsKey("ordinary-foreign-extra")).IsFalse();
        await Assert.That(before["Image"]!["User"]!.AsObject().ContainsKey("ordinary-other-user")).IsFalse();
        await Assert.That(before["Image"]!["User"]!.AsObject().ContainsKey("ordinary-other-tenant")).IsFalse();
        if (selectedUser.HasValue)
            await Assert.That(before["Image"]!["User"]!["ordinary-own-preference"]!["Value"]!.GetValue<string>()).IsEqualTo("own");
        string group = scope == SettingScope.User ? "User" : "Tenant";
        var after = new JsonObject
        {
            ["Id"] = request["NewIds"]![Key]!.DeepClone(),
            ["TenantId"] = tenant,
            ["Key"] = Key,
            ["Value"] = "\"native-contract\"",
            ["CreatedAt"] = NativeInstant,
            ["CreatedBy"] = request["Actor"]!.DeepClone(),
            ["UpdatedAt"] = null,
            ["UpdatedBy"] = null
        };
        if (selectedUser.HasValue) after["UserId"] = selectedUser.Value;
        else after["Locked"] = false;
        JsonNode expected = before["Image"]!.DeepClone();
        expected[group]![Key] = after.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request, tenant, selectedUser);
            await WriteAsync(caller, group, 0, null, after);
            await CheckAsync(caller, "complete", expected);
            await transaction.CommitAsync();
        }
        var persisted = await ReadAsync(caller, tenant, selectedUser, request["Keys"]);
        await Assert.That(JsonNode.DeepEquals(expected, persisted["Image"])).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OnlyAnOriginalParentLockProvesAnUnwrittenPreferenceRefusal(bool locked)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        SystemSetting? original = await migration.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == Key);
        migration.Tenants.Add(Tenant(tenant));
        if (original is null)
            migration.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = Key,
                Value = "\"\"",
                SettingValueTypeId = (int)SettingValueType.String,
                IsLocked = locked,
                CreatedAt = Instant,
                Description = AppearanceSettingDefinitions.ActiveProfileId.Description,
                Category = "Appearance"
            });
        await migration.SaveChangesAsync();
        if (original is not null)
            await migration.SystemSettings.Where(row => row.Id == original.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsLocked, locked));
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
            await caller.Database.OpenConnectionAsync();
            JsonObject request = Request(SettingScope.User, tenant, user);
            JsonObject before = await ReadAsync(caller, tenant, user, request["Keys"]);
            await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await AcquireAsync(caller, before, request, tenant, user);
                if (locked)
                {
                    await CheckAsync(caller, "complete", before["Image"]!);
                    await transaction.CommitAsync();
                }
                else
                {
                    await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(caller, "complete", before["Image"]!));
                    await transaction.RollbackAsync();
                }
            }
            await Assert.That(await migration.UserPreferences.CountAsync(row =>
                row.TenantId == tenant && row.UserId == user && row.SettingKey == Key)).IsEqualTo(0);
        }
        finally
        {
            if (original is null) await migration.SystemSettings.Where(row => row.SettingKey == Key).ExecuteDeleteAsync();
            else await migration.SystemSettings.Where(row => row.Id == original.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsLocked, original.IsLocked));
        }
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("owner-login")]
    [Arguments("executor-set")]
    [Arguments("source-type")]
    [Arguments("source-owner")]
    [Arguments("body-stamp")]
    [Arguments("disabled-trigger")]
    [Arguments("restrict-owner-policy")]
    [Arguments("missing-anchor")]
    [Arguments("missing-routine")]
    [Arguments("forged-body-stamp")]
    [Arguments("missing-disclosure-dependencies")]
    [Arguments("missing-disclosure-fence")]
    [Arguments("missing-disclosure-finalize")]
    [Arguments("extra-disclosure-owner-grant")]
    [Arguments("extra-disclosure-service-grant")]
    [Arguments("disclosure-helper-body")]
    [Arguments("disclosure-helper-search-path")]
    [Arguments("disclosure-helper-invoker")]
    [Arguments("disabled-tenant-trigger")]
    [Arguments("disabled-tenant-truncate-trigger")]
    public async Task ReadinessAndReinstallationRejectDriftWithoutRepair(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string routine = PostgresOrdinarySettingsContract.FunctionSql(migration, "read") + "(uuid,uuid,jsonb)";
        string table = sql.Table(typeof(SystemSetting));
        string dependencies = PostgresDiscoveryDisclosureContract.FunctionSql(migration, "dependencies");
        string fence = PostgresDiscoveryDisclosureContract.FunctionSql(migration, "fence_tenants");
        string finalize = PostgresDiscoveryDisclosureContract.FunctionSql(migration, "finalize");
        string disclosureRole = PostgresDiscoveryDisclosureContract.OwnerRole;
        const string replacedBody = "BEGIN RETURN NULL::jsonb; END;";
        const string replacedDependencies = "BEGIN RETURN; END;";
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            "owner-login" => $"ALTER ROLE {PostgresOrdinarySettingsContract.OwnerRole} LOGIN",
            "executor-set" => $"GRANT {PostgresOrdinarySettingsContract.GlobalRole} TO {sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "source-type" => $"ALTER TABLE {table} ALTER COLUMN description TYPE text",
            "source-owner" => $"ALTER TABLE {table} OWNER TO {PostgresOrdinarySettingsContract.OwnerRole}",
            "body-stamp" => $"COMMENT ON FUNCTION {routine} IS 'unrecognized-body'",
            "disabled-trigger" => $"ALTER TABLE {table} DISABLE TRIGGER ordinary_settings_changed",
            "restrict-owner-policy" => $"ALTER POLICY ordinary_settings_owner ON {sql.Table(typeof(UserPreference))} USING (false)",
            "missing-anchor" => $"DELETE FROM {sql.Name(PostgresOrdinarySettingsContract.SourceTable)}",
            "missing-routine" => $"DROP FUNCTION {PostgresOrdinarySettingsContract.FunctionSql(migration, "verify")}(jsonb)",
            "forged-body-stamp" => $"""
                CREATE OR REPLACE FUNCTION {PostgresOrdinarySettingsContract.FunctionSql(migration, "read")}(p_tenant uuid,p_user uuid,p_keys jsonb)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${replacedBody}$body$;
                COMMENT ON FUNCTION {routine} IS 'ordinary-settings:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(replacedBody)))}';
                """,
            "missing-disclosure-dependencies" => $"""
                SET LOCAL ROLE {disclosureRole};
                REVOKE EXECUTE ON FUNCTION {dependencies}(text,uuid[]) FROM {PostgresOrdinarySettingsContract.OwnerRole};
                RESET ROLE;
                """,
            "missing-disclosure-fence" => $"""
                SET LOCAL ROLE {disclosureRole};
                REVOKE EXECUTE ON FUNCTION {fence}(uuid[]) FROM {PostgresOrdinarySettingsContract.OwnerRole};
                RESET ROLE;
                """,
            "missing-disclosure-finalize" => $"""
                SET LOCAL ROLE {disclosureRole};
                REVOKE EXECUTE ON FUNCTION {finalize}(uuid[],uuid[],boolean[],bigint[],bigint[]) FROM {PostgresOrdinarySettingsContract.OwnerRole};
                RESET ROLE;
                """,
            "extra-disclosure-owner-grant" => $"""
                SET LOCAL ROLE {disclosureRole};
                GRANT EXECUTE ON FUNCTION {PostgresDiscoveryDisclosureContract.FunctionSql(migration, "catalog")}()
                    TO {PostgresOrdinarySettingsContract.OwnerRole};
                RESET ROLE;
                """,
            "extra-disclosure-service-grant" => $"""
                SET LOCAL ROLE {disclosureRole};
                GRANT EXECUTE ON FUNCTION {dependencies}(text,uuid[]) TO {sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)};
                RESET ROLE;
                """,
            "disclosure-helper-body" => $"""
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {disclosureRole};
                SET LOCAL ROLE {disclosureRole};
                CREATE OR REPLACE FUNCTION {dependencies}(p_source text,p_ids uuid[])
                    RETURNS SETOF uuid LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${replacedDependencies}$body$;
                COMMENT ON FUNCTION {dependencies}(text,uuid[])
                    IS 'ordinary-settings:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(replacedDependencies)))}';
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {disclosureRole};
                """,
            "disclosure-helper-search-path" => $"""
                SET LOCAL ROLE {disclosureRole};
                ALTER FUNCTION {fence}(uuid[]) SET search_path = public,pg_catalog;
                RESET ROLE;
                """,
            "disclosure-helper-invoker" => $"""
                SET LOCAL ROLE {disclosureRole};
                ALTER FUNCTION {finalize}(uuid[],uuid[],boolean[],bigint[],bigint[]) SECURITY INVOKER;
                RESET ROLE;
                """,
            "disabled-tenant-trigger" => $"ALTER TABLE {sql.Table(typeof(Tenant))} DISABLE TRIGGER ordinary_settings_tenant_changed",
            "disabled-tenant-truncate-trigger" => $"ALTER TABLE {sql.Table(typeof(Tenant))} DISABLE TRIGGER ordinary_settings_tenant_truncated",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync(change);
            await transaction.CreateSavepointAsync("drift");
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOrdinarySettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await transaction.RollbackToSavepointAsync("drift");
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOrdinarySettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await transaction.RollbackAsync();
        }
        await PostgresOrdinarySettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task MissingNamespaceCannotFallBackToTheInstalledContract()
    {
        await InstallAsync();
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global, "ordinary_missing_namespace"));
        await using var caller = new ExploreDbContext(builder.Options);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresOrdinarySettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
    }

    private const string MissingKey = "ordinary-native-missing-source";
    private const string Key = "appearance.active_profile_id";
    private const string NativeInstant = "2026-10-12T10:00:00.0000000Z";
    private static readonly DateTime Instant = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresOrdinarySettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant = null)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime)
            builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        return new ExploreDbContext(builder.Options) { TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null };
    }

    private static async Task<JsonObject> ReadAsync(ExploreDbContext context, Guid? tenant = null, Guid? user = null, JsonNode? keys = null)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresOrdinarySettingsContract.FunctionSql(context, "read")}(@tenant,@user,@keys)::text";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Uuid) { Value = (object?)user ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Jsonb)
        { Value = keys?.ToJsonString() ?? JsonSerializer.Serialize(new[] { MissingKey }) });
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
    }

    private static Task AcquireAsync(ExploreDbContext context, JsonObject before, JsonObject? request = null, Guid? tenant = null, Guid? user = null) =>
        ExecuteAsync(context, "acquire",
        [
            new("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value },
            new("user", NpgsqlDbType.Uuid) { Value = (object?)user ?? DBNull.Value },
            new("request", NpgsqlDbType.Jsonb)
            {
                Value = (request ?? EmptyRequest()).ToJsonString()
            },
            new("before", NpgsqlDbType.Jsonb) { Value = before.ToJsonString() }
        ]);

    private static JsonObject EmptyRequest() => JsonSerializer.SerializeToNode(new
    {
        Keys = new[] { MissingKey },
        Effects = Array.Empty<object>(),
        NewIds = new Dictionary<string, Guid>(),
        Actor = Guid.CreateVersion7(),
        OccurredAt = NativeInstant
    })!.AsObject();

    private static JsonObject Request(SettingScope scope, Guid? tenant = null, Guid? user = null,
        string key = Key, string value = "\"native-contract\"") =>
        JsonSerializer.SerializeToNode(new
        {
            Keys = new[] { key },
            Effects = new[] { new HumanSettingsMutation(scope == SettingScope.Instance ? HumanSettingsOperation.InstanceSettings
                : scope == SettingScope.User ? HumanSettingsOperation.UserSettings : HumanSettingsOperation.TenantSettings,
                scope, tenant, user, key, HumanSettingsMutationKind.SetValue, value) },
            NewIds = new Dictionary<string, Guid> { [key] = Guid.CreateVersion7() },
            Actor = user ?? Guid.CreateVersion7(),
            OccurredAt = NativeInstant
        })!.AsObject();

    private static JsonObject SystemAfter(JsonObject before, JsonObject request, string key = Key)
    {
        JsonNode value = request["Effects"]!.AsArray().Single(effect => effect!["Key"]!.GetValue<string>() == key)!["Value"]!;
        if (before["Image"]!["System"]![key] is JsonObject original)
        {
            var after = original.DeepClone().AsObject();
            after["Value"] = value.DeepClone(); after["UpdatedAt"] = NativeInstant;
            after["UpdatedBy"] = request["Actor"]!.DeepClone();
            return after;
        }
        return new JsonObject
        {
            ["Id"] = request["NewIds"]![key]!.DeepClone(),
            ["Key"] = key,
            ["Value"] = value.DeepClone(),
            ["Type"] = (int)SettingRegistry.Get(key)!.ValueType,
            ["Locked"] = false,
            ["AllowedValues"] = null,
            ["Description"] = SettingRegistry.Get(key)!.Description,
            ["Category"] = SettingRegistry.Get(key)!.Category,
            ["DisplayOrder"] = 0,
            ["CreatedAt"] = NativeInstant,
            ["CreatedBy"] = request["Actor"]!.DeepClone(),
            ["UpdatedAt"] = null,
            ["UpdatedBy"] = null
        };
    }

    private static Task WriteAsync(ExploreDbContext context, string group, int operation, JsonNode? before, JsonNode? after, string key = Key) =>
        ExecuteAsync(context, "write",
        [
            new("group", NpgsqlDbType.Text) { Value = group }, new("key", NpgsqlDbType.Text) { Value = key },
            new("operation", NpgsqlDbType.Integer) { Value = operation },
            new("before", NpgsqlDbType.Jsonb) { Value = before?.ToJsonString() ?? "null" },
            new("after", NpgsqlDbType.Jsonb) { Value = after?.ToJsonString() ?? "null" }
        ]);

    private static Task CheckAsync(ExploreDbContext context, string operation, JsonNode expected) =>
        ExecuteAsync(context, operation, [new("expected", NpgsqlDbType.Jsonb) { Value = expected.ToJsonString() }]);

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = "Ordinary native fixture",
        Slug = $"ordinary-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private static async Task ExecuteAsync(ExploreDbContext context, string operation, NpgsqlParameter[] parameters)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresOrdinarySettingsContract.FunctionSql(context, operation)}"
            + $"({string.Join(",", parameters.Select(parameter => "@" + parameter.ParameterName))})";
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }
}
