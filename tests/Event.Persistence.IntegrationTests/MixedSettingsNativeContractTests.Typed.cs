using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

public sealed partial class MixedSettingsNativeContractTests
{
    [Test]
    [Arguments("full-governance")]
    [Arguments("missing-producer")]
    [Arguments("missing-patch")]
    [Arguments("actor-substitution")]
    public async Task TypedNativeOriginalCannotBeInferredFromTheGlobalLogin(string change)
    {
        var seeded = await SeedAsync();
        var payload = JsonNode.Parse(TypedRequest(new UpdateEventPolicyCommand
        {
            UserId = seeded.Actor,
            Patch = new() { AllowUserSubmittedEvents = OptionalUpdate<bool>.Set(false) }
        }))!.AsObject();
        if (change == "full-governance") payload["Producer"] = "full-governance";
        if (change == "missing-producer") payload.Remove("Producer");
        if (change == "missing-patch") payload["EventPolicy"]!.AsObject().Remove("Patch");
        if (change == "actor-substitution") payload["Actor"] = Guid.CreateVersion7().ToString("D");
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        await Assert.ThrowsAsync<PostgresException>(() => ReadTypedAsync(caller, seeded, payload.ToJsonString(), null));
    }

    // Restricted SQL data custody only. These rows do not pretend to authenticate HTTP;
    // the Local API controls invoke the actual original handlers and credential observer.
    [Test]
    [Arguments("commit")]
    [Arguments("wrong-value")]
    [Arguments("wrong-lock")]
    [Arguments("metadata")]
    [Arguments("omitted")]
    [Arguments("empty-write")]
    [Arguments("null-write")]
    [Arguments("replay")]
    [Arguments("reacquire")]
    [Arguments("reacquire-immediate")]
    public async Task TypedNativePatchRetainsPairCompletionAndPhysicalTombstone(string outcome)
    {
        var seeded = await SeedAsync();
        string key = GovernanceSettingKeys.Events.CardClickOpensDetailPage;
        await using (var migration = fixture.CreateDbContext())
        {
            var row = await migration.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
            if (row is null)
                migration.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    Value = "false",
                    IsLocked = false,
                    ValueType = SettingValueType.Boolean,
                    Category = "Events",
                    Description = "retained native metadata",
                    CreatedBy = seeded.Actor,
                    CreatedAt = DateTime.UtcNow
                });
            else { row.Value = "false"; row.IsLocked = false; row.Description = "retained native metadata"; }
            await migration.SaveChangesAsync();
        }
        string request = TypedRequest(new UpdateEventPolicyCommand
        {
            UserId = seeded.Actor,
            Patch = new PatchEventPolicyDto
            {
                EventCardClickOpensDetailPage = OptionalUpdate<bool>.Set(true),
                LockTenantEventCardClickBehavior = OptionalUpdate<bool>.Set(true)
            }
        });
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string original = await ReadTypedAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, seeded, request, original);
        string accepted = JsonSerializer.Serialize(new[] { new TypedSettingsEffect(key, "true", true) });
        await TypedCallAsync(caller, "typed_compile", accepted, request);
        var data = JsonNode.Parse(original)!["Data"]!.AsObject();
        var rows = data[nameof(SystemSetting)]!.AsArray();
        var before = rows.OfType<JsonObject>().Single(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == key);
        var after = before.DeepClone().AsObject();
        after[nameof(SystemSetting.Value)] = outcome == "wrong-value" ? "false" : "true";
        after[nameof(SystemSetting.IsLocked)] = outcome != "wrong-lock";
        after[nameof(SystemSetting.UpdatedBy)] = seeded.Actor.ToString("D");
        DateTime now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        after[nameof(SystemSetting.UpdatedAt)] = now.ToString("O", CultureInfo.InvariantCulture);
        if (outcome == "metadata") after[nameof(SystemSetting.Description)] = "unadmitted";
        var changes = new JsonArray(new JsonObject
        {
            ["Key"] = key,
            ["Type"] = nameof(SystemSetting),
            ["Id"] = before["Id"]!.DeepClone(),
            ["Before"] = before.DeepClone(),
            ["After"] = after.DeepClone()
        });
        Task attack;
        if (outcome is "null-write" or "empty-write")
            attack = TypedCallAsync(caller, "typed_write", outcome == "null-write" ? "null" : "[]");
        else if (outcome == "omitted")
            attack = TypedCallAsync(caller, "typed_complete",
                new JsonObject { ["Data"] = data.DeepClone(), ["Rejected"] = false }.ToJsonString());
        else if (outcome is "wrong-value" or "wrong-lock" or "metadata")
            attack = TypedCallAsync(caller, "typed_write", changes.ToJsonString());
        else
        {
            await TypedCallAsync(caller, "typed_write", changes.ToJsonString());
            if (outcome == "replay")
                attack = TypedCallAsync(caller, "typed_write", changes.ToJsonString());
            else
            {
                rows.Remove(before);
                rows.Add(after);
                data[nameof(SystemSetting)] = new JsonArray(rows.OfType<JsonObject>()
                    .OrderBy(row => row["Id"]!.ToString(), StringComparer.Ordinal).Select(row => row.DeepClone()).ToArray());
                await TypedCallAsync(caller, "typed_complete",
                    new JsonObject { ["Data"] = data.DeepClone(), ["Rejected"] = false }.ToJsonString());
                if (outcome == "commit")
                {
                    await owner.CommitAsync();
                    await using var committed = fixture.CreateDbContext();
                    var actual = await committed.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
                    await Assert.That(actual.Value).IsEqualTo("true");
                    await Assert.That(actual.IsLocked).IsTrue();
                    await Assert.That(actual.Description).IsEqualTo("retained native metadata");
                    return;
                }
                if (outcome == "reacquire-immediate")
                    await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
                string second = await ReadTypedAsync(caller, seeded, request, null);
                attack = ReadTypedAsync(caller, seeded, request, second);
            }
        }
        await Assert.ThrowsAsync<PostgresException>(() => attack);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        var unchanged = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(unchanged.Value).IsEqualTo("false");
        await Assert.That(unchanged.IsLocked).IsFalse();
        await Assert.That(unchanged.Description).IsEqualTo("retained native metadata");
    }

    [Test]
    [Arguments("value")]
    [Arguments("lock")]
    [Arguments("key")]
    [Arguments("original")]
    public async Task TypedNativePatchCompilationCannotSubstituteOriginalPresenceOrValues(string substitution)
    {
        var seeded = await SeedAsync();
        var command = new UpdateEventPolicyCommand
        {
            UserId = seeded.Actor,
            Patch = new() { AllowUserSubmittedEvents = OptionalUpdate<bool>.Set(false) }
        };
        string request = TypedRequest(command);
        await using var caller = Open(PrimaryDatabaseRole.Global, null);
        string before = await ReadTypedAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, seeded, request, before);
        string accepted = JsonSerializer.Serialize(new[]
        {
            new TypedSettingsEffect(substitution == "key" ? GovernanceSettingKeys.Events.GroupSubmissionEnabled
                : GovernanceSettingKeys.Events.UserSubmissionEnabled, substitution == "value" ? "true" : "false",
                substitution == "lock")
        });
        if (substitution == "original")
            request = TypedRequest(command with { Patch = new() { AllowUserSubmittedEvents = OptionalUpdate<bool>.Set(true) } });
        await Assert.ThrowsAsync<PostgresException>(() => TypedCallAsync(caller, "typed_compile", accepted, request));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("complete")]
    [Arguments("missing-cleanup")]
    [Arguments("ceiling-key")]
    [Arguments("tenant-lock")]
    [Arguments("revision")]
    public async Task TypedNativeOnboardingFootprintIsClosedToItsOriginalProducer(string change)
    {
        var seeded = await SeedAsync();
        var settings = new UpdateTenantPolicyRequest { ForceAnnouncementBarRedisplay = true };
        var command = new CompleteTenantOnboardingCommand
        {
            UserId = seeded.Actor,
            Settings = settings,
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "Native data original",
                OperatorKindCode = "individual",
                JurisdictionCountryCode = "GB",
                PublicContactEmail = "operator@example.test"
            }
        };
        string request = JsonSerializer.Serialize(new
        {
            EventPolicy = (UpdateEventPolicyCommand?)null,
            Onboarding = command,
            Reporting = (UpdateTenantReportingIntakePolicyCommand?)null,
            Actor = seeded.Actor,
            TenantId = (Guid?)seeded.Tenant,
            Producer = "tenant-onboarding"
        });
        await using var caller = Open(PrimaryDatabaseRole.Global, seeded.Tenant);
        string before = await ReadTypedAsync(caller, seeded, request, null);
        using var image = JsonDocument.Parse(before);
        var system = MixedSettingsDataImage.Rows<SystemSetting>(image.RootElement.GetProperty("Data").GetRawText())
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var constrained = image.RootElement.GetProperty("Data").GetProperty(nameof(SystemSetting)).EnumerateArray()
            .First(row => row.GetProperty(nameof(SystemSetting.AllowedValues)).ValueKind == JsonValueKind.Array);
        await Assert.That(system[constrained.GetProperty(nameof(SystemSetting.SettingKey)).GetString()!].AllowedValues)
            .IsEqualTo(constrained.GetProperty(nameof(SystemSetting.AllowedValues)).GetRawText());
        var effects = TenantPolicySettingService.CompileOriginal(settings, system, "native-original", null).ToList();
        if (change == "missing-cleanup") effects.RemoveAll(effect => effect.Key == GovernanceSettingKeys.Domains.TenantCustomDomain);
        if (change == "ceiling-key") effects.Add(new(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, "true"));
        if (change == "tenant-lock") effects[0] = effects[0] with { IsLocked = true };
        if (change == "revision")
        {
            int revision = effects.FindIndex(effect => effect.Key == GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision);
            effects[revision] = effects[revision] with { Value = "2" };
        }
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, seeded, request, before);
        Task compile = TypedCallAsync(caller, "typed_compile", JsonSerializer.Serialize(effects), request);
        if (change == "complete") await compile;
        else await Assert.ThrowsAsync<PostgresException>(() => compile);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.TenantId == seeded.Tenant)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TypedNativeOnboardingRemovalRetainsBothPresenceAndAbsence(bool absent)
    {
        var seeded = await SeedAsync();
        string key = GovernanceSettingKeys.Domains.TenantCustomDomain;
        await using (var migration = fixture.CreateDbContext())
        {
            if (!absent)
                migration.TenantSettingOverrides.Add(new TenantSetting
                {
                    TenantId = seeded.Tenant,
                    Tenant = null!,
                    SettingKey = key,
                    Value = "\"old.example.test\"",
                    CreatedBy = seeded.Actor,
                    CreatedAt = DateTime.UtcNow
                });
            await migration.SaveChangesAsync();
        }
        var settings = new UpdateTenantPolicyRequest();
        var onboarding = new CompleteTenantOnboardingCommand
        {
            UserId = seeded.Actor,
            Settings = settings,
            DirectoryOperatorIdentity = new() { PublicName = "Native original" }
        };
        string request = JsonSerializer.Serialize(new
        {
            EventPolicy = (UpdateEventPolicyCommand?)null,
            Onboarding = onboarding,
            Reporting = (UpdateTenantReportingIntakePolicyCommand?)null,
            Actor = seeded.Actor,
            TenantId = (Guid?)seeded.Tenant,
            Producer = "tenant-onboarding"
        });
        await using var caller = Open(PrimaryDatabaseRole.Global, seeded.Tenant);
        string before = await ReadTypedAsync(caller, seeded, request, null);
        var data = JsonNode.Parse(before)!["Data"]!;
        var system = MixedSettingsDataImage.Rows<SystemSetting>(data.ToJsonString())
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var effects = TenantPolicySettingService.CompileOriginal(settings, system, "native-original", null);
        await Assert.That(effects.Single(effect => effect.Key == key).Remove).IsTrue();
        var old = data[nameof(TenantSetting)]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(row => row[nameof(TenantSetting.SettingKey)]!.GetValue<string>() == key);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, seeded, request, before);
        await TypedCallAsync(caller, "typed_compile", JsonSerializer.Serialize(effects), request);
        await TypedCallAsync(caller, "typed_write", new JsonArray(new JsonObject
        {
            ["Type"] = nameof(TenantSetting),
            ["Key"] = key,
            ["TenantId"] = seeded.Tenant.ToString("D"),
            ["Id"] = old?["Id"]?.DeepClone() ?? JsonValue.Create(Guid.CreateVersion7().ToString("D")),
            ["Before"] = old?.DeepClone(),
            ["After"] = null
        }).ToJsonString());
        using var current = JsonDocument.Parse(await ReadTypedAsync(caller, seeded, request, null));
        await Assert.That(current.RootElement.GetProperty("Data").GetProperty(nameof(TenantSetting)).EnumerateArray()
            .Any(row => row.GetProperty(nameof(TenantSetting.SettingKey)).GetString() == key)).IsFalse();
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.TenantId == seeded.Tenant && row.SettingKey == key))
            .IsEqualTo(!absent);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TypedNativeReportingRetainsActualTargetAndEnabledValue(bool substitute)
    {
        var seeded = await SeedAsync();
        string key = GovernanceSettingKeys.EventReporting.IntakeEnabled;
        await using (var migration = fixture.CreateDbContext())
        {
            await migration.SystemSettings.Where(row => row.SettingKey == key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, false));
            migration.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = seeded.Tenant,
                Tenant = null!,
                SettingKey = key,
                Value = "true",
                CreatedBy = seeded.Actor,
                CreatedAt = DateTime.UtcNow
            });
            await migration.SaveChangesAsync();
        }
        var command = new UpdateTenantReportingIntakePolicyCommand(seeded.Tenant, seeded.Actor, new() { Enabled = false });
        string request = JsonSerializer.Serialize(new
        {
            EventPolicy = (UpdateEventPolicyCommand?)null,
            Onboarding = (CompleteTenantOnboardingCommand?)null,
            Reporting = command,
            Actor = seeded.Actor,
            TenantId = (Guid?)seeded.Tenant,
            Producer = "reporting-intake"
        });
        await using var caller = Open(PrimaryDatabaseRole.Global, seeded.Tenant);
        string before = await ReadTypedAsync(caller, seeded, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, seeded, request, before);
        string effect = JsonSerializer.Serialize(new[]
        {
            new TypedSettingsEffect(GovernanceSettingKeys.EventReporting.IntakeEnabled, substitute ? "true" : "false")
        });
        Task compile = TypedCallAsync(caller, "typed_compile", effect, request);
        if (substitute)
        {
            await Assert.ThrowsAsync<PostgresException>(() => compile);
            await owner.RollbackAsync();
        }
        else
        {
            await compile;
            var data = JsonNode.Parse(before)!["Data"]!.AsObject();
            var rows = data[nameof(TenantSetting)]!.AsArray();
            var old = rows.OfType<JsonObject>().Single(row => row[nameof(TenantSetting.SettingKey)]!.GetValue<string>() == key);
            var after = old.DeepClone().AsObject();
            after[nameof(TenantSetting.Value)] = "false";
            after[nameof(TenantSetting.UpdatedBy)] = seeded.Actor.ToString("D");
            DateTime now = DateTime.UtcNow;
            after[nameof(TenantSetting.UpdatedAt)] = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)
                .ToString("O", CultureInfo.InvariantCulture);
            await TypedCallAsync(caller, "typed_write", new JsonArray(new JsonObject
            {
                ["Type"] = nameof(TenantSetting),
                ["Key"] = key,
                ["TenantId"] = seeded.Tenant.ToString("D"),
                ["Id"] = old["Id"]!.DeepClone(),
                ["Before"] = old.DeepClone(),
                ["After"] = after.DeepClone()
            }).ToJsonString());
            rows.Remove(old);
            rows.Add(after);
            await TypedCallAsync(caller, "typed_complete",
                new JsonObject { ["Data"] = data.DeepClone(), ["Rejected"] = false }.ToJsonString());
            await owner.CommitAsync();
        }
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.Where(row => row.TenantId == seeded.Tenant && row.SettingKey == key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(substitute ? "true" : "false");
    }

    private static string TypedRequest(UpdateEventPolicyCommand command) => JsonSerializer.Serialize(new
    {
        EventPolicy = command,
        Onboarding = (CompleteTenantOnboardingCommand?)null,
        Reporting = (UpdateTenantReportingIntakePolicyCommand?)null,
        Actor = command.UserId,
        TenantId = (Guid?)null,
        Producer = "event-policy"
    });

    private async Task<string> ReadTypedAsync(ExploreDbContext caller, Seeded source, string request, string? before)
    {
        if (caller.Database.GetDbConnection().State != ConnectionState.Open)
            await caller.Database.OpenConnectionAsync();
        await PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = caller.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, before is null ? "typed_read" : "typed_acquire")}"
            + $"(@actor,@provider,@account,@tenant,@stamp,@session,@resolved,@request{(before is null ? "" : ",@before")})::text";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, source.Actor);
        Add("provider", NpgsqlDbType.Integer, (int)AuthenticationProviderKind.Keycloak);
        Add("account", NpgsqlDbType.Text, source.Account);
        Add("tenant", NpgsqlDbType.Uuid, caller.TenantFilterTenantId);
        Add("stamp", NpgsqlDbType.Text, null);
        Add("session", NpgsqlDbType.Uuid, null);
        Add("resolved", NpgsqlDbType.Uuid, caller.TenantFilterTenantId);
        Add("request", NpgsqlDbType.Jsonb, request);
        if (before is not null) Add("before", NpgsqlDbType.Jsonb, before);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Typed native source is absent."));
    }

    private static async Task TypedCallAsync(ExploreDbContext caller, string routine, string payload, string? request = null)
    {
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = caller.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresMixedSettingsContract.FunctionSql(caller, routine)}(@tenant,"
            + (request is null ? "" : "@request,") + "@payload)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)caller.TenantFilterTenantId ?? DBNull.Value });
        if (request is not null) command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = request });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        await command.ExecuteNonQueryAsync();
    }
}
