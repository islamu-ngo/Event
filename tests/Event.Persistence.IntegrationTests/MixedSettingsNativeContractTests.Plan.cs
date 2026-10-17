using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class MixedSettingsNativeContractTests
{
    [Test]
    [Arguments(nameof(TenantPlanAssignment))]
    [Arguments(nameof(TenantPlanVersion))]
    [Arguments(nameof(TenantPlanVersionSetting))]
    [Arguments(nameof(TenantPlanVersionQuota))]
    [Arguments(nameof(TenantPlan))]
    public async Task NativePlanReadinessRequiresEveryOriginalPlanSourceExclusion(string sourceName)
    {
        var source = await SeedAsync();
        Type type = sourceName switch
        {
            nameof(TenantPlanAssignment) => typeof(TenantPlanAssignment),
            nameof(TenantPlanVersion) => typeof(TenantPlanVersion),
            nameof(TenantPlanVersionSetting) => typeof(TenantPlanVersionSetting),
            nameof(TenantPlanVersionQuota) => typeof(TenantPlanVersionQuota),
            _ => typeof(TenantPlan)
        };
        await using var migration = fixture.CreateDbContext();
        string table = new HumanPrincipalSourceSql(migration).Table(type);
        await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DISABLE TRIGGER mixed_settings_changed");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global, source.Tenant);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresMixedSettingsContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ENABLE ALWAYS TRIGGER mixed_settings_changed");
        }
    }

    [Test]
    [Arguments("assignment")]
    [Arguments("version")]
    [Arguments("content")]
    [Arguments("absent-setting")]
    [Arguments("quota")]
    public async Task NativePlanAcquisitionRetainsOriginalPlanRowsAndAbsences(string changed)
    {
        var source = await SeedAsync();
        var plan = await SeedNativePlanAsync(source);
        string request = NativePlanRequest(source, plan.Assignment);
        await using var caller = Open(PrimaryDatabaseRole.Global, source.Tenant);
        string before = await ReadTypedAsync(caller, source, request, null);
        await using (var other = fixture.CreateDbContext())
        {
            if (changed == "assignment")
                await other.TenantPlanAssignments.Where(row => row.Id == plan.Assignment)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedBy, (Guid?)source.Actor));
            if (changed == "version")
                await other.TenantPlanVersions.Where(row => row.Id == plan.Version)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActiveForProvisioning, false));
            if (changed == "content")
                await other.TenantPlanVersionSettings.Where(row => row.TenantPlanVersionId == plan.Version)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, false));
            if (changed == "absent-setting")
            {
                other.TenantPlanVersionSettings.Add(new TenantPlanVersionSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantPlanVersionId = plan.Version,
                    SettingKey = GovernanceSettingKeys.Email.SmtpPort,
                    JsonValue = "2525",
                    IsLocked = true,
                    CreatedAt = DateTime.UtcNow
                });
                await other.SaveChangesAsync();
            }
            if (changed == "quota")
            {
                other.TenantPlanVersionQuotas.Add(new TenantPlanVersionQuota
                {
                    Id = Guid.CreateVersion7(),
                    TenantPlanVersionId = plan.Version,
                    QuotaKey = TenantPlanQuotaKeys.StorageBytes,
                    Limit = 1024,
                    CreatedAt = DateTime.UtcNow
                });
                await other.SaveChangesAsync();
            }
        }
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => ReadTypedAsync(caller, source, request, before));
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.CountAsync(row => row.TenantId == source.Tenant
            && row.SettingKey == GovernanceSettingKeys.Events.CardClickOpensDetailPage)).IsEqualTo(0);
    }

    [Test]
    [Arguments("commit")]
    [Arguments("typed-commit")]
    [Arguments("typed-wrong-lock")]
    [Arguments("wrong-lock")]
    [Arguments("wrong-value")]
    [Arguments("missing-audit")]
    [Arguments("audit-target")]
    [Arguments("audit-actor")]
    [Arguments("audit-content")]
    [Arguments("replay")]
    [Arguments("reacquire")]
    [Arguments("omitted-email-revision")]
    public async Task NativePlanWritesRequireExactLocksOriginalAuditAndPrivateCompletion(string outcome)
    {
        var source = await SeedAsync();
        bool email = outcome == "omitted-email-revision";
        var plan = await SeedNativePlanAsync(source, email);
        string key = email ? GovernanceSettingKeys.Email.SmtpPort : GovernanceSettingKeys.Events.CardClickOpensDetailPage;
        string value = email ? "2525" : "false";
        string request = NativePlanRequest(source, plan.Assignment);
        await using var caller = Open(PrimaryDatabaseRole.Global, source.Tenant);
        string original = await ReadTypedAsync(caller, source, request, null);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadTypedAsync(caller, source, request, original);
        await TypedCallAsync(caller, "typed_compile", JsonSerializer.Serialize(new[] { new TypedSettingsEffect(key, value, true) }), request);
        var data = JsonNode.Parse(original)!["Data"]!.AsObject();
        DateTime now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var after = new JsonObject
        {
            ["Id"] = Guid.CreateVersion7().ToString("D"),
            ["TenantId"] = source.Tenant.ToString("D"),
            ["SettingKey"] = key,
            ["Value"] = outcome == "wrong-value" ? "true" : value,
            ["IsLocked"] = outcome is not ("wrong-lock" or "typed-wrong-lock"),
            ["CreatedAt"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["CreatedBy"] = source.Actor.ToString("D"),
            ["UpdatedAt"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["UpdatedBy"] = source.Actor.ToString("D")
        };
        string row = new JsonArray(new JsonObject
        {
            ["Type"] = nameof(TenantSetting),
            ["Id"] = after["Id"]!.DeepClone(),
            ["Key"] = key,
            ["TenantId"] = source.Tenant.ToString("D"),
            ["Before"] = null,
            ["After"] = after.DeepClone()
        }).ToJsonString();
        string writer = outcome.StartsWith("typed-", StringComparison.Ordinal) ? "typed_write" : "plan_write";
        Task attack;
        if (outcome is "wrong-lock" or "typed-wrong-lock" or "wrong-value")
            attack = TypedCallAsync(caller, writer, row);
        else
        {
            await TypedCallAsync(caller, writer, row);
            data[nameof(TenantSetting)]!.AsArray().Add(after);
            if (outcome == "replay")
                attack = TypedCallAsync(caller, "plan_write", row);
            else
            {
                var beforeAudit = data[nameof(TenantPlanAssignment)]!.AsArray().OfType<JsonObject>().Single();
                var audit = beforeAudit.DeepClone().AsObject();
                audit["UpdatedAt"] = now.ToString("O", CultureInfo.InvariantCulture);
                audit["UpdatedBy"] = (outcome == "audit-actor" ? Guid.CreateVersion7() : source.Actor).ToString("D");
                if (outcome == "audit-target") audit["Id"] = Guid.CreateVersion7().ToString("D");
                if (outcome == "audit-content") audit["AssignedAt"] = now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture);
                string auditWrite = new JsonArray(new JsonObject
                {
                    ["Type"] = nameof(TenantPlanAssignment),
                    ["Id"] = audit["Id"]!.DeepClone(),
                    ["Before"] = beforeAudit.DeepClone(),
                    ["After"] = audit.DeepClone()
                }).ToJsonString();
                if (outcome is "audit-target" or "audit-actor" or "audit-content")
                    attack = TypedCallAsync(caller, "plan_write", auditWrite);
                else
                {
                    if (outcome != "missing-audit")
                    {
                        await TypedCallAsync(caller, "plan_write", auditWrite);
                        data[nameof(TenantPlanAssignment)] = new JsonArray(audit);
                    }
                    string completed = new JsonObject { ["Data"] = data.DeepClone(), ["Rejected"] = false }.ToJsonString();
                    if (outcome is "missing-audit" or "omitted-email-revision")
                        attack = TypedCallAsync(caller, "typed_complete", completed);
                    else
                    {
                        await TypedCallAsync(caller, "typed_complete", completed);
                        if (outcome is "commit" or "typed-commit")
                        {
                            await owner.CommitAsync();
                            await using var independent = fixture.CreateDbContext();
                            var stored = await independent.TenantSettingOverrides.AsNoTracking().SingleAsync(item =>
                                item.TenantId == source.Tenant && item.SettingKey == key);
                            await Assert.That(stored.Value).IsEqualTo(value);
                            await Assert.That(stored.IsLocked).IsTrue();
                            await Assert.That(await independent.TenantPlanAssignments.Where(item => item.Id == plan.Assignment)
                                .Select(item => item.UpdatedBy).SingleAsync()).IsEqualTo(source.Actor);
                            return;
                        }
                        string second = await ReadTypedAsync(caller, source, request, null);
                        attack = ReadTypedAsync(caller, source, request, second);
                    }
                }
            }
        }
        await Assert.ThrowsAsync<PostgresException>(() => attack);
        await owner.RollbackAsync();
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.CountAsync(item => item.TenantId == source.Tenant && item.SettingKey == key)).IsEqualTo(0);
        await Assert.That(await observer.TenantPlanAssignments.Where(item => item.Id == plan.Assignment).Select(item => item.UpdatedBy).SingleAsync()).IsNull();
    }

    private static string NativePlanRequest(Seeded source, Guid assignment) => JsonSerializer.Serialize(new
    {
        EventPolicy = (object?)null,
        Onboarding = (object?)null,
        Reporting = (object?)null,
        PlanAssignment = new ApplyInstanceTenantPlanAssignmentCommand(source.Tenant, assignment, source.Actor),
        Actor = source.Actor,
        TenantId = source.Tenant,
        Producer = "tenant-plan-assignment"
    });

    private async Task<(Guid Assignment, Guid Version)> SeedNativePlanAsync(Seeded source, bool email = false)
    {
        await using var seed = fixture.CreateDbContext();
        string key = email ? GovernanceSettingKeys.Email.SmtpPort : GovernanceSettingKeys.Events.CardClickOpensDetailPage;
        var system = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        if (system is not null) system.IsLocked = false;
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(),
            Key = $"native-plan-{Guid.CreateVersion7():N}",
            DisplayName = "Native original plan",
            CreatedAt = DateTime.UtcNow
        };
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlanId = plan.Id,
            TenantPlan = plan,
            VersionNumber = 1,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
            CreatedAt = DateTime.UtcNow
        };
        version.Settings.Add(new TenantPlanVersionSetting
        {
            Id = Guid.CreateVersion7(),
            TenantPlanVersionId = version.Id,
            SettingKey = key,
            JsonValue = email ? "2525" : "false",
            IsLocked = true,
            CreatedAt = DateTime.UtcNow
        });
        var assignment = new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = source.Tenant,
            TenantPlanId = plan.Id,
            TenantPlan = plan,
            TenantPlanVersionId = version.Id,
            TenantPlanVersion = version,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = source.Actor,
            CreatedAt = DateTime.UtcNow
        };
        seed.AddRange(plan, version, assignment);
        await seed.SaveChangesAsync();
        return (assignment.Id, version.Id);
    }
}
