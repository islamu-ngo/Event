using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

/// <summary>Real restricted-login data custody; authenticated action controls live in the API corpus.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class MixedSettingsNativeContractTests(PostgreSqlContainerFixture fixture)
{
    private const string EmailKey = GovernanceSettingKeys.TenantDelegation.LockSmtp;
    private const string OtherKey = GovernanceSettingKeys.TenantDelegation.LockAnalytics;

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherEnrolledPurposesCannotBorrowMixedSources(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var caller = Open(role, role == PrimaryDatabaseRole.Runtime ? seeded.Tenant : null);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, Request(), null));
    }

    [Test]
    [Arguments("Values")]
    [Arguments("Category")]
    public async Task MissingOriginalRequestMemberCannotBorrowSqlNull(string member)
    {
        var seeded = await SeedAsync();
        var request = Request();
        var payload = JsonSerializer.SerializeToNode(request)!.AsObject();
        payload.Remove(member);
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            ReadAsync(caller, seeded, request, null, serializedRequest: payload.ToJsonString()));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EmailKey || row.SettingKey == OtherKey)
            .CountAsync(row => row.Value == "true")).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MixedSourcesUseGlobalForBothExactPhysicalTargets(bool tenant)
    {
        var seeded = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global, tenant ? seeded.Tenant : null);
        var request = Request() with { Scope = tenant ? SettingScope.Tenant : SettingScope.Instance };
        using var original = JsonDocument.Parse(await ReadAsync(caller, seeded, request, null));
        await Assert.That(await caller.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"").SingleAsync())
            .IsEqualTo(fixture.NativeQueueEnrollment.GlobalLogin);
        await Assert.That(original.RootElement.GetProperty("Principal").GetProperty("q0")[0].GetProperty("Id").GetGuid())
            .IsEqualTo(seeded.Actor);
        if (tenant)
            foreach (var row in original.RootElement.GetProperty("Data").GetProperty(nameof(TenantSetting)).EnumerateArray())
                await Assert.That(row.GetProperty(nameof(TenantSetting.TenantId)).GetGuid()).IsEqualTo(seeded.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, request, null, Guid.CreateVersion7()));
    }

    [Test]
    [Arguments("setting")]
    [Arguments("absence")]
    [Arguments("metadata")]
    [Arguments("role")]
    [Arguments("binding")]
    [Arguments("support")]
    [Arguments("projection")]
    [Arguments("secret-binding")]
    public async Task EveryOriginalSourceChangeInvalidatesTheIdleObservation(string source)
    {
        var seeded = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        var request = Request();
        string original = await ReadAsync(caller, seeded, request, null);
        await using (var competitor = fixture.CreateDbContext())
        {
            var names = new HumanPrincipalSourceSql(competitor);
            Type type = source switch
            {
                "role" => typeof(PlatformUserRole),
                "binding" => typeof(UserExternalLogin),
                "support" => typeof(SupportAccessSession),
                "projection" => typeof(EmailDispatchProcessorState),
                "secret-binding" => typeof(SecretBinding),
                _ => typeof(SystemSetting)
            };
            if (source == "metadata")
                await competitor.SystemSettings.Where(row => row.SettingKey == OtherKey)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "mixed-native-replacement"));
            else if (source == "setting")
                await competitor.SystemSettings.Where(row => row.SettingKey == OtherKey)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
            else
                // A genuine statement with no matching row still tests absence/range exclusion.
                await competitor.Database.ExecuteSqlRawAsync(
                    $"UPDATE {names.Table(type)} SET {names.Column(type, "Id")} = {names.Column(type, "Id")} WHERE false");
        }
        await using var attempt = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, request, original));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await attempt.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IndependentMixedExclusionSurvivesUntilPhysicalSettlement(bool commit)
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null,
            request, new Dictionary<string, string>(), CancellationToken.None);
        await using var competitor = fixture.CreateDbContext();
        string anchor = new HumanPrincipalSourceSql(competitor).Name(PostgresMixedSettingsContract.SourceTable);
        await using (var blocked = await competitor.Database.BeginTransactionAsync())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                competitor.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await blocked.RollbackAsync();
        }
        using var image = JsonDocument.Parse(original);
        await PostgresMixedSettingsContract.CompleteAsync(caller, fixture.NativeQueueEnrollment, null,
            image.RootElement.GetProperty("Data").GetRawText(), CancellationToken.None);
        // Completion does not release the transaction's source reservation early.
        await using (var stillBlocked = await competitor.Database.BeginTransactionAsync())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                competitor.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await stillBlocked.RollbackAsync();
        }
        if (commit) await owner.CommitAsync();
        else await owner.RollbackAsync();
        await using var released = await competitor.Database.BeginTransactionAsync();
        await competitor.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT");
        await released.RollbackAsync();
    }

    [Test]
    [Arguments("foreign-key")]
    [Arguments("wrong-value")]
    [Arguments("foreign-slice")]
    [Arguments("lock-widening")]
    [Arguments("description-widening")]
    [Arguments("category-widening")]
    [Arguments("type-widening")]
    [Arguments("allowed-values-widening")]
    [Arguments("display-order-widening")]
    [Arguments("creation-actor-widening")]
    [Arguments("creation-time-widening")]
    [Arguments("identity-widening")]
    [Arguments("replayed-compilation")]
    [Arguments("omitted-slice")]
    public async Task NativeCompilationCannotWidenReplayOrOmitAnAcceptedGroup(string attack)
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        var accepted = new Dictionary<string, string> { [EmailKey] = "false", [OtherKey] = "false" };
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request, accepted, CancellationToken.None);
        var data = JsonNode.Parse(original)!["Data"]!.AsObject();
        var emailBefore = data[nameof(SystemSetting)]!.AsArray().OfType<JsonObject>()
            .Single(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == EmailKey);
        var emailAfter = emailBefore.DeepClone().AsObject();
        emailAfter[nameof(SystemSetting.Value)] = attack == "wrong-value" ? "true" : "false";
        emailAfter[nameof(SystemSetting.UpdatedBy)] = seeded.Actor.ToString("D");
        DateTime instant = DateTime.UtcNow;
        instant = new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
        emailAfter[nameof(SystemSetting.UpdatedAt)] = instant.ToString("O", CultureInfo.InvariantCulture);
        if (attack == "foreign-key") emailAfter[nameof(SystemSetting.SettingKey)] = GovernanceSettingKeys.TenantDelegation.LockStorage;
        if (attack == "lock-widening") emailAfter[nameof(SystemSetting.IsLocked)] = true;
        switch (attack)
        {
            case "description-widening": emailAfter[nameof(SystemSetting.Description)] = "unadmitted"; break;
            case "category-widening": emailAfter[nameof(SystemSetting.Category)] = "unadmitted"; break;
            case "type-widening": emailAfter[nameof(SystemSetting.SettingValueTypeId)] = (int)SettingValueType.String; break;
            case "allowed-values-widening": emailAfter[nameof(SystemSetting.AllowedValues)] = "[]"; break;
            case "display-order-widening":
                emailAfter[nameof(SystemSetting.DisplayOrder)] =
                    emailBefore[nameof(SystemSetting.DisplayOrder)]!.GetValue<int>() + 1;
                break;
            case "creation-actor-widening": emailAfter[nameof(SystemSetting.CreatedBy)] = Guid.CreateVersion7().ToString("D"); break;
            case "creation-time-widening":
                emailAfter[nameof(SystemSetting.CreatedAt)] =
                    DateTime.Parse(emailBefore[nameof(SystemSetting.CreatedAt)]!.GetValue<string>(),
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).AddTicks(10)
                        .ToString("O", CultureInfo.InvariantCulture);
                break;
            case "identity-widening": emailAfter[nameof(SystemSetting.Id)] = Guid.CreateVersion7().ToString("D"); break;
        }
        var changes = new JsonObject
        {
            ["Keys"] = new JsonArray(JsonValue.Create(EmailKey)),
            ["Rows"] = new JsonArray(new JsonObject
            {
                ["Type"] = nameof(SystemSetting),
                ["Before"] = emailBefore.DeepClone(),
                ["After"] = emailAfter
            }),
            ["Complete"] = true
        };
        Task attackTask;
        if (attack == "replayed-compilation")
            attackTask = PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request, accepted, CancellationToken.None);
        else if (attack == "omitted-slice")
        {
            await PostgresMixedSettingsContract.WriteAsync(caller, fixture.NativeQueueEnrollment, null,
                "email", changes.ToJsonString(), CancellationToken.None);
            string proposed = await PostgresMixedSettingsContract.ReadDataAsync(caller, fixture.NativeQueueEnrollment, request, null, CancellationToken.None);
            attackTask = PostgresMixedSettingsContract.CompleteAsync(caller, fixture.NativeQueueEnrollment, null, proposed, CancellationToken.None);
        }
        else
            attackTask = PostgresMixedSettingsContract.WriteAsync(caller, fixture.NativeQueueEnrollment, null,
                attack == "foreign-slice" ? "settings" : "email", changes.ToJsonString(), CancellationToken.None);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => attackTask);
        await Assert.That(failure!.SqlState).IsEqualTo(attack == "identity-widening"
            ? PostgresErrorCodes.SerializationFailure : PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EmailKey || row.SettingKey == OtherKey)
            .CountAsync(row => row.Value == "true")).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeProjectionCannotBorrowNullSliceOrEmptyKeys(bool nullSlice)
    {
        var seeded = await SeedAsync();
        await using (var migration = fixture.CreateDbContext())
        {
            if (!await migration.EmailDispatchProcessorStates.AnyAsync(row => row.ProcessorCode == "smtp"))
            {
                migration.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
                {
                    Id = Guid.CreateVersion7(),
                    ProcessorCode = "smtp",
                    DeliveryPolicyRevision = 1,
                    UpdatedAt = DateTime.UtcNow
                });
                await migration.SaveChangesAsync();
            }
        }
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null,
            request, new Dictionary<string, string> { [EmailKey] = "false" }, CancellationToken.None);
        var before = JsonNode.Parse(original)!["Data"]![nameof(EmailDispatchProcessorState)]!.AsArray()
            .OfType<JsonObject>().Single(row => row[nameof(EmailDispatchProcessorState.ProcessorCode)]!.GetValue<string>() == "smtp");
        var after = before.DeepClone().AsObject();
        long revision = before[nameof(EmailDispatchProcessorState.DeliveryPolicyRevision)]!.GetValue<long>();
        after[nameof(EmailDispatchProcessorState.DeliveryPolicyRevision)] = revision + 1;
        after[nameof(EmailDispatchProcessorState.UpdatedBy)] = seeded.Actor.ToString("D");
        DateTime instant = DateTime.UtcNow;
        instant = new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
        after[nameof(EmailDispatchProcessorState.UpdatedAt)] = instant.ToString("O", CultureInfo.InvariantCulture);
        var changes = new JsonObject
        {
            ["Keys"] = nullSlice ? new JsonArray(JsonValue.Create(EmailKey)) : new JsonArray(),
            ["Rows"] = new JsonArray(new JsonObject
            {
                ["Type"] = nameof(EmailDispatchProcessorState),
                ["Before"] = before.DeepClone(),
                ["After"] = after
            }),
            ["Complete"] = true
        };
        await using var write = caller.Database.GetDbConnection().CreateCommand();
        write.Transaction = owner.GetDbTransaction();
        write.CommandText = $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "write")}(@tenant,@slice,@changes)";
        write.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = DBNull.Value });
        write.Parameters.Add(new NpgsqlParameter("slice", NpgsqlDbType.Text) { Value = nullSlice ? DBNull.Value : "email" });
        write.Parameters.Add(new NpgsqlParameter("changes", NpgsqlDbType.Jsonb) { Value = changes.ToJsonString() });
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync());
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
        await using var independent = fixture.CreateDbContext();
        await Assert.That(await independent.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == EmailKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalHasNoAmbientScalarOrPrivateReceiptAuthority(bool tenant)
    {
        var seeded = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global, tenant ? seeded.Tenant : null);
        await caller.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        var tenantDenial = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "ready")}(@tenant)",
            new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = Guid.CreateVersion7() }));
        await Assert.That(tenantDenial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await ReadAsync(caller, seeded, Request() with { Scope = tenant ? SettingScope.Tenant : SettingScope.Instance }, null);
        var names = new HumanPrincipalSourceSql(caller);
        var scalarDenial = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {names.Table(typeof(SystemSetting))} SET {names.Column(typeof(SystemSetting), "Value")} = 'false' WHERE false"));
        await Assert.That(scalarDenial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var sourceDenial = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT revision FROM {names.Name(PostgresMixedSettingsContract.SourceTable)}"));
        await Assert.That(sourceDenial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var attemptDenial = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {names.Name("mixed_settings_attempt")}"));
        await Assert.That(attemptDenial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("none")]
    [Arguments("previous")]
    [Arguments("current")]
    [Arguments("clear-history")]
    [Arguments("future-cutoff")]
    [Arguments("operator-state")]
    [Arguments("without-source")]
    [Arguments("replay")]
    [Arguments("omitted-projection")]
    public async Task MixedEmailProjectionRetainsOriginalRevisionAndSuppressionHistory(string transition)
    {
        var seeded = await SeedAsync();
        DateTime cutoff = new(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        await using (var migration = fixture.CreateDbContext())
        {
            var processor = await migration.EmailDispatchProcessorStates.SingleOrDefaultAsync(row => row.ProcessorCode == "smtp");
            if (processor is null)
            {
                processor = new EmailDispatchProcessorState { Id = Guid.CreateVersion7(), ProcessorCode = "smtp" };
                migration.EmailDispatchProcessorStates.Add(processor);
            }
            processor.DeliveryPolicyRevision = 7;
            processor.OptionalSuppressedThroughRevision = 5;
            processor.OptionalSuppressedThroughUtc = cutoff;
            processor.IsPaused = false;
            processor.UpdatedAt = cutoff;
            await migration.SaveChangesAsync();
        }
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        DateTime now = await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request,
            new Dictionary<string, string> { [EmailKey] = "false" }, CancellationToken.None);
        var data = JsonNode.Parse(original)!["Data"]!.AsObject();
        var settings = data[nameof(SystemSetting)]!.AsArray();
        var settingBefore = settings.OfType<JsonObject>().Single(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == EmailKey);
        var settingAfter = settingBefore.DeepClone().AsObject();
        settingAfter[nameof(SystemSetting.Value)] = "false";
        settingAfter[nameof(SystemSetting.UpdatedBy)] = seeded.Actor.ToString("D");
        settingAfter[nameof(SystemSetting.UpdatedAt)] = now.ToString("O", CultureInfo.InvariantCulture);
        async Task Write(string type, JsonNode? before, JsonNode? after, bool complete = false) =>
            await PostgresMixedSettingsContract.WriteAsync(caller, fixture.NativeQueueEnrollment, null, "email",
                new JsonObject
                {
                    ["Keys"] = new JsonArray(JsonValue.Create(EmailKey)),
                    ["Rows"] = new JsonArray(new JsonObject
                    {
                        ["Type"] = type, ["Before"] = before?.DeepClone(), ["After"] = after?.DeepClone()
                    }),
                    ["Complete"] = complete
                }.ToJsonString(), CancellationToken.None);
        if (transition != "without-source")
            await Write(nameof(SystemSetting), settingBefore, settingAfter);
        var processors = data[nameof(EmailDispatchProcessorState)]!.AsArray();
        var before = processors.OfType<JsonObject>().Single(row => row[nameof(EmailDispatchProcessorState.ProcessorCode)]!.GetValue<string>() == "smtp");
        var after = before.DeepClone().AsObject();
        after[nameof(EmailDispatchProcessorState.DeliveryPolicyRevision)] = 8;
        after[nameof(EmailDispatchProcessorState.UpdatedBy)] = seeded.Actor.ToString("D");
        after[nameof(EmailDispatchProcessorState.UpdatedAt)] = now.ToString("O", CultureInfo.InvariantCulture);
        if (transition is "previous" or "current")
        {
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughRevision)] = transition == "previous" ? 7 : 8;
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughUtc)] = now.ToString("O", CultureInfo.InvariantCulture);
        }
        if (transition == "clear-history")
        {
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughRevision)] = null;
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughUtc)] = null;
        }
        if (transition == "future-cutoff")
        {
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughRevision)] = 8;
            after[nameof(EmailDispatchProcessorState.OptionalSuppressedThroughUtc)] = now.AddTicks(10).ToString("O", CultureInfo.InvariantCulture);
        }
        if (transition == "operator-state") after[nameof(EmailDispatchProcessorState.IsPaused)] = true;
        bool commit = transition is "none" or "previous" or "current";
        if (commit)
        {
            await Write(nameof(EmailDispatchProcessorState), before, after, complete: true);
            settings[settings.IndexOf(settingBefore)] = settingAfter;
            processors[processors.IndexOf(before)] = after;
            await PostgresMixedSettingsContract.CompleteAsync(caller, fixture.NativeQueueEnrollment, null,
                data.ToJsonString(), CancellationToken.None);
            await owner.CommitAsync();
        }
        else
        {
            Task attack;
            if (transition == "omitted-projection")
            {
                await PostgresMixedSettingsContract.WriteAsync(caller, fixture.NativeQueueEnrollment, null, "email",
                    new JsonObject
                    {
                        ["Keys"] = new JsonArray(JsonValue.Create(EmailKey)),
                        ["Rows"] = new JsonArray(), ["Complete"] = true
                    }.ToJsonString(), CancellationToken.None);
                settings[settings.IndexOf(settingBefore)] = settingAfter;
                attack = PostgresMixedSettingsContract.CompleteAsync(caller, fixture.NativeQueueEnrollment, null,
                    data.ToJsonString(), CancellationToken.None);
            }
            else if (transition == "replay")
            {
                await Write(nameof(EmailDispatchProcessorState), before, after);
                var repeated = after.DeepClone().AsObject();
                repeated[nameof(EmailDispatchProcessorState.DeliveryPolicyRevision)] = 9;
                attack = Write(nameof(EmailDispatchProcessorState), after, repeated);
            }
            else attack = Write(nameof(EmailDispatchProcessorState), before, after);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => attack);
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await owner.RollbackAsync();
        }
        await using var independent = fixture.CreateDbContext();
        var stored = await independent.EmailDispatchProcessorStates.AsNoTracking().SingleAsync(row => row.ProcessorCode == "smtp");
        await Assert.That(stored.DeliveryPolicyRevision).IsEqualTo(commit ? 8L : 7L);
        await Assert.That(stored.OptionalSuppressedThroughRevision)
            .IsEqualTo(transition == "previous" ? 7L : transition == "current" ? 8L : 5L);
        await Assert.That(stored.OptionalSuppressedThroughUtc)
            .IsEqualTo(transition is "previous" or "current" ? now : cutoff);
        await Assert.That(stored.IsPaused).IsFalse();
        await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == EmailKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(commit ? "false" : "true");
    }

    [Test]
    public async Task InstanceMixedSettingWriteCannotAdoptATenantOverride()
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        DateTime now = await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request,
            new Dictionary<string, string> { [EmailKey] = "false" }, CancellationToken.None);
        var row = JsonSerializer.SerializeToNode(new TenantSetting
        {
            Id = Guid.CreateVersion7(), TenantId = seeded.Tenant, Tenant = null!,
            SettingKey = EmailKey, Value = "false", CreatedAt = now, CreatedBy = seeded.Actor,
            UpdatedAt = now, UpdatedBy = seeded.Actor
        })!.AsObject();
        row.Remove(nameof(TenantSetting.Tenant));
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => PostgresMixedSettingsContract.WriteAsync(
            caller, fixture.NativeQueueEnrollment, null, "email", new JsonObject
            {
                ["Keys"] = new JsonArray(JsonValue.Create(EmailKey)),
                ["Rows"] = new JsonArray(new JsonObject { ["Type"] = nameof(TenantSetting), ["Before"] = null, ["After"] = row }),
                ["Complete"] = true
            }.ToJsonString(), CancellationToken.None));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
        await using var independent = fixture.CreateDbContext();
        await Assert.That(await independent.TenantSettingOverrides.AnyAsync(setting =>
            setting.TenantId == seeded.Tenant && setting.SettingKey == EmailKey)).IsFalse();
    }

    [Test]
    [Arguments("consumer")]
    [Arguments("raw-setting")]
    public async Task AcquiredMixedAttemptDoesNotGrantAmbientEmailAuthority(string attack)
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request,
            new Dictionary<string, string> { [EmailKey] = "false" }, CancellationToken.None);
        var names = new HumanPrincipalSourceSql(caller);
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = owner.GetDbTransaction();
        command.CommandText = attack == "consumer"
            ? $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, "email_consume")}"
                + $"('{names.Table(typeof(SystemSetting))}'::regclass::oid,'UPDATE',NULL,'{{}}'::jsonb)"
            : $"UPDATE {names.Table(typeof(SystemSetting))} SET {names.Column(typeof(SystemSetting), "Value")} = 'false'"
                + $" WHERE {names.Column(typeof(SystemSetting), "SettingKey")} = @key";
        if (attack == "raw-setting")
            command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = EmailKey });
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EmailKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
    }

    [Test]
    public async Task OmittingNativeCompletionCannotCommitAnAcceptedPartialGroup()
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null, request,
            new Dictionary<string, string> { [EmailKey] = "false", [OtherKey] = "false" }, CancellationToken.None);
        var before = JsonNode.Parse(original)!["Data"]![nameof(SystemSetting)]!.AsArray().OfType<JsonObject>()
            .Single(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == EmailKey);
        var after = before.DeepClone().AsObject();
        after[nameof(SystemSetting.Value)] = "false";
        after[nameof(SystemSetting.UpdatedBy)] = seeded.Actor.ToString("D");
        DateTime now = DateTime.UtcNow;
        after[nameof(SystemSetting.UpdatedAt)] = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);
        await PostgresMixedSettingsContract.WriteAsync(caller, fixture.NativeQueueEnrollment, null, "email",
            new JsonObject
            {
                ["Keys"] = new JsonArray(JsonValue.Create(EmailKey)),
                ["Rows"] = new JsonArray(new JsonObject
                {
                    ["Type"] = nameof(SystemSetting),
                    ["Before"] = before.DeepClone(),
                    ["After"] = after
                }),
                ["Complete"] = true
            }.ToJsonString(), CancellationToken.None);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => owner.CommitAsync());
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EmailKey || row.SettingKey == OtherKey)
            .CountAsync(row => row.Value == "true")).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompletionCannotReleaseTheOriginalPhysicalAttemptForReacquisition(bool immediateSettlement)
    {
        var seeded = await SeedAsync();
        var request = Request();
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(caller, seeded, request, original);
        await PostgresMixedSettingsContract.CompileAsync(caller, fixture.NativeQueueEnrollment, null,
            request, new Dictionary<string, string>(), CancellationToken.None);
        string expected = await PostgresMixedSettingsContract.ReadDataAsync(caller,
            fixture.NativeQueueEnrollment, request, null, CancellationToken.None);
        await PostgresMixedSettingsContract.CompleteAsync(caller, fixture.NativeQueueEnrollment,
            null, expected, CancellationToken.None);
        if (immediateSettlement)
            await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, request, original));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EmailKey || row.SettingKey == OtherKey)
            .CountAsync(row => row.Value == "true")).IsEqualTo(2);
    }

    [Test]
    [Arguments("owner-login")]
    [Arguments("public-execute")]
    [Arguments("disabled-exclusion")]
    [Arguments("source-column")]
    [Arguments("source-check")]
    [Arguments("source-index-order")]
    [Arguments("ambient-scalar")]
    [Arguments("email-peer-execute")]
    [Arguments("email-peer-missing")]
    [Arguments("email-owner-login")]
    public async Task NativeAdmissionRejectsPrivilegeCatalogAndSourceDriftWithoutRepair(string drift)
    {
        var seeded = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        string signature = PostgresMixedSettingsContract.FunctionSql(migration, "data") + "(uuid,jsonb)";
        string consumer = PostgresMixedSettingsContract.FunctionSql(migration, "email_consume")
            + "(" + PostgresMixedSettingsContract.EmailConsumeSignature + ")";
        string sourceTable = names.Name(PostgresMixedSettingsContract.SourceTable);
        string indexName = migration.Model.FindEntityType(typeof(SystemSetting))!.GetIndexes()
            .Single(index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SystemSetting.SettingKey)])).GetDatabaseName()!;
        string settingIndex = names.Name(indexName);
        string indexColumn = names.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey));
        string change = drift switch
        {
            "owner-login" => $"ALTER ROLE {PostgresMixedSettingsContract.OwnerRole} LOGIN",
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "email-peer-execute" => $"GRANT EXECUTE ON FUNCTION {consumer} TO {PostgresMixedSettingsContract.GlobalRole}",
            "email-peer-missing" => $"REVOKE EXECUTE ON FUNCTION {consumer} FROM {PostgresEmailDeliveryPolicyContract.OwnerRole}",
            "email-owner-login" => $"ALTER ROLE {PostgresEmailDeliveryPolicyContract.OwnerRole} LOGIN",
            "disabled-exclusion" => $"ALTER TABLE {names.Table(typeof(SystemSetting))} DISABLE TRIGGER mixed_settings_changed",
            "source-column" => $"ALTER TABLE {sourceTable} ADD COLUMN foreign_state integer",
            "source-check" => $"ALTER TABLE {sourceTable} DROP CONSTRAINT mixed_settings_source_revision_check",
            "source-index-order" => $"DROP INDEX {settingIndex}; CREATE UNIQUE INDEX {names.Quote(indexName)} ON {names.Table(typeof(SystemSetting))} ({indexColumn} DESC)",
            _ => $"GRANT UPDATE ({names.Column(typeof(SystemSetting), "Value")}) ON {names.Table(typeof(SystemSetting))} TO \"{fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        };
        string restore = drift switch
        {
            "owner-login" => $"ALTER ROLE {PostgresMixedSettingsContract.OwnerRole} NOLOGIN",
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC",
            "email-peer-execute" => $"REVOKE EXECUTE ON FUNCTION {consumer} FROM {PostgresMixedSettingsContract.GlobalRole}",
            "email-peer-missing" => $"GRANT EXECUTE ON FUNCTION {consumer} TO {PostgresEmailDeliveryPolicyContract.OwnerRole}",
            "email-owner-login" => $"ALTER ROLE {PostgresEmailDeliveryPolicyContract.OwnerRole} NOLOGIN",
            "disabled-exclusion" => $"ALTER TABLE {names.Table(typeof(SystemSetting))} ENABLE ALWAYS TRIGGER mixed_settings_changed",
            "source-column" => $"ALTER TABLE {sourceTable} DROP COLUMN foreign_state",
            "source-check" => $"ALTER TABLE {sourceTable} ADD CONSTRAINT mixed_settings_source_revision_check CHECK (revision >= 0)",
            "source-index-order" => $"DROP INDEX {settingIndex}; CREATE UNIQUE INDEX {names.Quote(indexName)} ON {names.Table(typeof(SystemSetting))} ({indexColumn})",
            _ => $"REVOKE UPDATE ({names.Column(typeof(SystemSetting), "Value")}) ON {names.Table(typeof(SystemSetting))} FROM \"{fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global, null);
            await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, Request(), null));
            await Assert.ThrowsAsync<PostgresException>(() => PostgresMixedSettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    [Test]
    [Arguments("ASC NULLS LAST")]
    [Arguments("DESC NULLS LAST")]
    public async Task BootstrapGenerationUniqueIndexRequiresOriginalOrder(string order)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(InstanceBootstrapState));
        string index = await migration.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.format('%I.%I', n.nspname, c.relname) AS "Value"
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE i.indrelid = pg_catalog.to_regclass({0})
                AND i.indisunique AND NOT i.indisprimary
            """, table).SingleAsync();
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"DROP INDEX {index}");
            await migration.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX mixed_bootstrap_generation_wrong_order ON {table}
                    ({names.Column(typeof(InstanceBootstrapState), nameof(InstanceBootstrapState.Generation))} {order})
                """);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_source_key_drift");
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments("is_to_be_announced = true")]
    [Arguments("is_deleted = false AND is_to_be_announced = false")]
    public async Task EventLocationUniqueIndexRequiresTheCompleteOriginalPredicate(string predicate)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(EventLocation));
        string index = await migration.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.format('%I.%I', n.nspname, c.relname) AS "Value"
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE i.indrelid = pg_catalog.to_regclass({0})
                AND i.indisunique AND i.indnkeyatts = 2 AND i.indpred IS NOT NULL
            """, table).SingleAsync();
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"DROP INDEX {index}");
            await migration.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX mixed_event_location_wrong_predicate ON {table}
                    ({names.Column(typeof(EventLocation), nameof(EventLocation.TenantId))},
                     {names.Column(typeof(EventLocation), nameof(EventLocation.EventId))})
                    WHERE {predicate}
                """);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_source_key_drift");
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments("status_id = 2")]
    [Arguments("status_id = 1 AND ended_at_utc IS NULL")]
    public async Task SupportSessionUniqueIndexRequiresTheCompleteActivePredicate(string predicate)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(SupportAccessSession));
        string index = await migration.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.format('%I.%I', n.nspname, c.relname) AS "Value"
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE i.indrelid = pg_catalog.to_regclass({0})
                AND i.indisunique AND i.indpred IS NOT NULL
            """, table).SingleAsync();
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"DROP INDEX {index}");
            await migration.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX mixed_support_session_wrong_predicate ON {table}
                    ({names.Column(typeof(SupportAccessSession), nameof(SupportAccessSession.ActorUserId))})
                    WHERE {predicate}
                """);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_source_key_drift");
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(nameof(Location), nameof(Location.DisplaySortKey))]
    [Arguments(nameof(LocationPii), nameof(LocationPii.AddressSubstringKey))]
    public async Task UnicodeCollationGuardsRespectTheOriginalPrivacyBoundary(string entityName, string propertyName)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        Type type = entityName switch
        {
            nameof(Location) => typeof(Location),
            nameof(LocationPii) => typeof(LocationPii),
            _ => throw new ArgumentOutOfRangeException(nameof(entityName))
        };
        var seeded = await SeedAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string column = names.Column(type, propertyName);
        string storeType = migration.Model.FindEntityType(type)!.FindProperty(propertyName)!
            .GetRelationalTypeMapping().StoreType;
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE {names.Table(type)} ALTER COLUMN {column}
                    TYPE {storeType} COLLATE "default" USING {column}
                """);
            if (type == typeof(Location))
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
                await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_source_catalog_drift");
            }
            else
            {
                await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
                await using var caller = Open(PrimaryDatabaseRole.Global, null);
                var observed = await ReadAsync(caller, seeded, Request(), null);
                await Assert.That(JsonNode.Parse(observed)!["Data"]!.AsObject().ContainsKey(nameof(LocationPii))).IsFalse();
            }
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments("SET GENERATED ALWAYS")]
    [Arguments("DROP IDENTITY")]
    public async Task PermissionIdentityRequiresTheDeclaredGenerationMode(string change)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE {names.Table(typeof(Permission))}
                    ALTER COLUMN {names.Column(typeof(Permission), nameof(Permission.Id))} {change}
                """);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_source_catalog_drift");
            await transaction.RollbackAsync();
        }
        await PostgresMixedSettingsContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments("data")]
    [Arguments("ready")]
    public async Task RestampingAChangedRoutineCannotForgeTheOriginalMixedBody(string routine)
    {
        var seeded = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresMixedSettingsContract.FunctionSql(migration, routine);
        string signature = function + (routine == "ready" ? "(uuid)" : "(uuid,jsonb)");
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}({(routine == "ready" ? "p_tenant uuid" : "p_tenant uuid,p_request jsonb")})
                RETURNS {(routine == "ready" ? "void" : "jsonb")}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body$
                BEGIN {(routine == "ready" ? "RETURN;" : "RETURN pg_catalog.jsonb_build_object();")} END;
                $body$;
            DO $stamp$ DECLARE fingerprint text; BEGIN
                SELECT 'mixed-settings:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex')
                    INTO fingerprint FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}');
                EXECUTE pg_catalog.format('COMMENT ON FUNCTION {signature} IS %L',fingerprint);
            END $stamp$;
            """);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global, null);
            await caller.Database.OpenConnectionAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("mixed_settings_exact_body_drift");
            await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller, seeded, Request(), null));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    public async Task MixedInstallationPreservesProviderAndStorageExclusionBodies()
    {
        await using var migration = fixture.CreateDbContext();
        var before = await ExclusionDefinitionsAsync(migration);
        foreach (string trigger in new[] { "provider_configuration_changed", "storage_configuration_changed", "storage_probe_source_changed" })
            await Assert.That(before.Any(item => item.StartsWith(trigger + ":", StringComparison.Ordinal))).IsTrue();
        await PostgresMixedSettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var after = await ExclusionDefinitionsAsync(migration);
        await Assert.That(after.SequenceEqual(before)).IsTrue();
        // Existing readiness must still attest its own body/catalog/privileges.
        await PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresStorageConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresStorageProbeSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task NativeMixedInstallerCannotBecomeAPortableSuccessFallback()
    {
        await using var sqlite = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>().UseSqlite().Options);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PostgresMixedSettingsContract.ApplyAsync(sqlite, fixture.NativeQueueEnrollment));
    }

    private static Task<List<string>> ExclusionDefinitionsAsync(ExploreDbContext context) =>
        context.Database.SqlQueryRaw<string>("""
            SELECT (t.tgname || ':' || pg_catalog.pg_get_triggerdef(t.oid) || ':' ||
                pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.prosrc,'UTF8')),'hex')) AS "Value"
            FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_proc p ON p.oid = t.tgfoid
            JOIN pg_catalog.pg_class r ON r.oid = t.tgrelid JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
            WHERE NOT t.tgisinternal AND t.tgname IN ('provider_configuration_changed','storage_configuration_changed','storage_probe_source_changed')
                AND n.nspname = {0}
            ORDER BY t.tgname,r.relname
            """, new HumanPrincipalSourceSql(context).Schema).ToListAsync();

    private async Task<Seeded> SeedAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresMixedSettingsContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var actor = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Mixed", LastName = "Native" }
        };
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Mixed native",
            Slug = $"mixed-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        string account = actor.Id.ToString("D");
        migration.AddRange(actor, tenant, new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = actor.Id,
            User = actor,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = account,
            CreatedAt = DateTime.UtcNow
        });
        foreach (string key in new[] { EmailKey, OtherKey })
        {
            var row = await migration.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
            if (row is null)
                migration.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    Category = "TenantDelegation",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = actor.Id
                });
            else { row.Value = "true"; row.IsLocked = false; }
        }
        await migration.SaveChangesAsync();
        return new(actor.Id, tenant.Id, account);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant is { } id ? new TenantIdentity(id) : null };
        context.BindTenantLease();
        return context;
    }

    private async Task<string> ReadAsync(ExploreDbContext caller, Seeded source,
        UpdateSettingBatchCommand request, string? before, Guid? targetOverride = null, string? serializedRequest = null)
    {
        if (caller.Database.GetDbConnection().State != ConnectionState.Open)
            await caller.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = caller.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, before is null ? "read" : "acquire")}"
            + $"(@actor,@provider,@account,@tenant,@stamp,@session,@resolved,@request{(before is null ? "" : ",@before")})::text";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, source.Actor);
        Add("provider", NpgsqlDbType.Integer, (int)AuthenticationProviderKind.Keycloak);
        Add("account", NpgsqlDbType.Text, source.Account);
        Add("tenant", NpgsqlDbType.Uuid, targetOverride ?? caller.TenantFilterTenantId);
        Add("stamp", NpgsqlDbType.Text, null);
        Add("session", NpgsqlDbType.Uuid, null);
        Add("resolved", NpgsqlDbType.Uuid, caller.TenantFilterTenantId);
        Add("request", NpgsqlDbType.Jsonb, serializedRequest ?? JsonSerializer.Serialize(request));
        if (before is not null) Add("before", NpgsqlDbType.Jsonb, before);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("The native mixed image is absent."));
    }

    private static UpdateSettingBatchCommand Request() => new()
    {
        Category = "TenantDelegation",
        Scope = SettingScope.Instance,
        Mode = BatchUpdateMode.Strict,
        Values = new Dictionary<string, string> { [EmailKey] = "false", [OtherKey] = "false" }
    };

    private sealed record Seeded(Guid Actor, Guid Tenant, string Account);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
