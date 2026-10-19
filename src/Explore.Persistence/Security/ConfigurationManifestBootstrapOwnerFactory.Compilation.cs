using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Seed;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Persistence.Security;

internal sealed partial class ConfigurationManifestBootstrapOwnerFactory
{
    private static ConfigurationManifestBootstrapSnapshot Snapshot(ConfigurationManifestBootstrapData data, JsonObject image)
    {
        var operations = Rows(image, "Operations").Where(row =>
                JsonNode.DeepEquals(row["Status"], data.ProviderValue("Operations", nameof(ConfigurationManifestOperation.Status),
                    ConfigurationManifestOperationStatus.Applied)) && row["BootstrapGeneration"] is not null)
            .OrderByDescending(row => Number(row, "BootstrapGeneration")).ThenByDescending(row => Long(row, "CompletedAt"))
            .ThenByDescending(row => Id(row, "Id"));
        var latest = operations.FirstOrDefault();
        return new(Rows(image, "Tenants").Select(row => new Tenant
        {
            Id = Id(row, "Id"),
            Slug = Text(row, "Slug")!,
            FullName = Text(row, "FullName")!,
            TenantStatusId = Number(row, "TenantStatusId"),
            TenantStatus = null!,
            CreatedAt = Time(row, "CreatedAt"),
            CreatedBy = NullableId(row, "CreatedBy"),
            UpdatedAt = NullableTime(row, "UpdatedAt"),
            UpdatedBy = NullableId(row, "UpdatedBy")
        }).ToImmutableArray(), Rows(image, "Systems").Select(System).ToImmutableArray(),
        Rows(image, "Overrides").Select(row => new TenantSetting
        {
            Id = Id(row, "Id"),
            TenantId = Id(row, "TenantId"),
            Tenant = null!,
            SettingKey = Text(row, "SettingKey")!,
            Value = Text(row, "Value")!,
            IsLocked = Bool(row, "IsLocked"),
            CreatedAt = Time(row, "CreatedAt"),
            CreatedBy = NullableId(row, "CreatedBy"),
            UpdatedAt = NullableTime(row, "UpdatedAt"),
            UpdatedBy = NullableId(row, "UpdatedBy")
        }).ToImmutableArray(), Rows(image, "Policies").Where(row => Bool(row, "IsActive"))
            .Select(row => Policy(image, row)).ToImmutableArray(),
        latest is null ? null : new(Text(latest, "InstanceSectionDigest")!, Number(latest, "BootstrapGeneration")));
    }

    private static PaidEventPolicyVersion Policy(JsonObject image, JsonObject row)
    {
        Guid id = Id(row, "Id");
        IEnumerable<JsonObject> Children(string tag) => Rows(image, tag)
            .Where(child => Id(child, "PaidEventPolicyVersionId") == id).OrderBy(child => Number(child, "Ordinal"));
        var result = PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(
            id, NullableId(row, "TenantId"), Number(row, "VersionNumber"), Bool(row, "IsPaymentsEnabled"),
            Children("Organizers").Select(child => (ActorTypeEnum)Number(child, "ActorTypeId")),
            Bool(row, "RequiresLocalVerification"), Children("Currencies").Select(child => Text(child, "CurrencyCode")!),
            Text(row, "DefaultCurrencyCode"), Children("Refunds").Select(child => (PaidEventRefundProtection)Number(child, "RefundProtectionId")),
            Children("Limits").Select(child => PaidEventPolicyCurrencyRiskLimit.Create(Text(child, "CurrencyCode")!,
                NullableLong(child, "PerEventSalesCeilingMinor"), NullableNumber(child, "PerEventSalesCountCeiling"),
                NullableLong(child, "RollingOrganizerSalesCeilingMinor"), NullableNumber(child, "RollingOrganizerSalesCountCeiling"),
                NullableNumber(child, "RollingOrganizerWindowDays"), NullableLong(child, "HighValueReviewThresholdMinor"))),
            Bool(row, "RequiresFirstPaidEventReview"), NullableNumber(row, "FarFutureReviewThresholdDays"));
        result.CreatedAt = Time(row, "CreatedAt"); result.CreatedBy = NullableId(row, "CreatedBy");
        result.UpdatedAt = NullableTime(row, "UpdatedAt"); result.UpdatedBy = NullableId(row, "UpdatedBy");
        return result;
    }

    private static Compiled Compile(ConfigurationManifestBootstrapData data, Reservations reserved, JsonObject before,
        ConfigurationManifestPreflightResult preflight, DateTime completedAt)
    {
        var plan = preflight.BoundPlan;
        var after = before.DeepClone().AsObject();
        var changes = new JsonArray();
        var systems = Rows(before, "Systems").Select(System).ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        foreach (var setting in plan.Instance.GuardedSettings.Concat(plan.Instance.UnguardedSettings))
        {
            var definition = SettingRegistry.Get(setting.Key)!;
            systems.TryGetValue(setting.Key, out var existing);
            bool resource = EventResourceSettingMutationGuard.Handles(setting.Key);
            bool publication = PublicationPolicySettingKeys.All.Contains(setting.Key, StringComparer.Ordinal);
            var row = existing ?? new SystemSetting
            {
                Id = publication ? PublicationId(setting.Key) : Guid.CreateVersion7(),
                SettingKey = setting.Key,
                Value = setting.JsonValue,
                ValueType = definition.ValueType,
                Category = definition.Category,
                Description = definition.Description,
                CreatedAt = plan.OccurredAt,
                CreatedBy = null
            };
            row.Value = setting.JsonValue;
            if (!resource)
            {
                row.ValueType = definition.ValueType; row.Category = definition.Category; row.Description = definition.Description;
                row.IsLocked = false;
            }
            if (publication)
            {
                row.AllowedValues = definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues);
                row.DisplayOrder = PublicationPolicySettingKeys.All.ToList().IndexOf(setting.Key) + 1;
            }
            row.UpdatedAt = existing is null && publication ? null : plan.OccurredAt;
            row.UpdatedBy = null;
            Add(data, after, changes, row);
        }
        if (plan.Instance.PaidEventPolicy?.ProposedRevision is { } instanceRevision)
        {
            var current = Policy(before, Rows(before, "Policies").Single(row => row["TenantId"] is null && Bool(row, "IsActive")));
            int version = current.VersionNumber + 1;
            current.Retire(); current.UpdatedAt = plan.OccurredAt; current.UpdatedBy = null;
            Add(data, after, changes, current);
            var revision = NewPolicy(reserved.InstancePolicy!.Value, null, version, instanceRevision, plan.OccurredAt);
            foreach (var entity in data.PolicyRows(revision)) Add(data, after, changes, entity);
        }
        int created = 0;
        foreach (var target in preflight.Tenants)
        {
            var tenant = target.Plan;
            bool create = target.Disposition == ConfigurationManifestTenantDisposition.Create;
            if (create)
            {
                created++;
                Add(data, after, changes, new Tenant
                {
                    Id = tenant.PlannedTenantId,
                    Slug = tenant.Slug,
                    FullName = tenant.DisplayName,
                    TenantStatusId = (int)TenantStatusEnum.Provisioning,
                    TenantStatus = null!,
                    CreatedAt = plan.OccurredAt,
                    CreatedBy = null
                });
                var branding = TenantSettingsDocument.Create(tenant.PlannedTenantId, tenant.BrandingDocument.DocumentKey,
                    tenant.BrandingDocument.SchemaVersion, tenant.BrandingDocument.DefaultsVersion, tenant.BrandingDocument.PayloadJson);
                branding.Id = tenant.BrandingDocument.DocumentId; branding.ConcurrencyStamp = Guid.CreateVersion7();
                branding.CreatedAt = plan.OccurredAt; branding.CreatedBy = null;
                Add(data, after, changes, branding);
                var directory = TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenant.PlannedTenantId, tenant.DisplayName);
                directory.Id = reserved.Directories[tenant.PlannedTenantId]; directory.ConcurrencyStamp = Guid.CreateVersion7();
                directory.CreatedAt = plan.OccurredAt; directory.CreatedBy = null;
                Add(data, after, changes, directory);
                foreach (var setting in tenant.GuardedSettings.Concat(tenant.UnguardedSettings))
                    Add(data, after, changes, new TenantSetting
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenant.PlannedTenantId,
                        Tenant = null!,
                        SettingKey = setting.Key,
                        Value = setting.JsonValue,
                        IsLocked = false,
                        CreatedAt = plan.OccurredAt,
                        CreatedBy = null,
                        UpdatedAt = EventResourceSettingMutationGuard.Handles(setting.Key) ? plan.OccurredAt : null,
                        UpdatedBy = null
                    });
                if (tenant.PaidEventPolicy is { } paid)
                    foreach (var entity in data.PolicyRows(NewPolicy(reserved.Policies[tenant.PlannedTenantId],
                                 tenant.PlannedTenantId, 1, paid, plan.OccurredAt)))
                        Add(data, after, changes, entity);
            }
        }
        var operation = Operation(plan, ConfigurationManifestOperationStatus.Applied, created, preflight.Tenants.Length - created,
            0, null, plan.Instance.ChangedSettingKeyNames, plan.Instance.ChangedDocumentKeyNames, completedAt);
        bool hasEffects = created > 0 || !plan.Instance.ChangedSettingKeyNames.IsEmpty || !plan.Instance.ChangedDocumentKeyNames.IsEmpty;
        if (hasEffects) operation.BindEffectOutbox(plan.EffectOutboxId);
        Add(data, after, changes, operation);
        foreach (var target in preflight.Tenants)
        {
            bool createdTenant = target.Disposition == ConfigurationManifestTenantDisposition.Create;
            Add(data, after, changes, ConfigurationManifestTenantResult.Create(plan.OperationId, target.TenantId,
                createdTenant ? ConfigurationManifestTenantResultStatus.Created : ConfigurationManifestTenantResultStatus.SkippedExisting,
                createdTenant ? target.Plan.ChangedSettingKeyNames : [],
                createdTenant ? target.Plan.ChangedDocumentKeyNames : [], completedAt));
        }
        if (hasEffects) Add(data, after, changes, ConfigurationManifestEffectOutbox.Create(plan.EffectOutboxId, plan.OperationId, completedAt));
        data.Order(after);
        return new(operation, changes, after);
    }

    private static PaidEventPolicyVersion NewPolicy(Guid id, Guid? tenant, int version,
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload, DateTime occurred)
    {
        var policy = PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(id, tenant, version, payload.IsPaymentsEnabled,
            payload.AllowedOrganizerKindIds.Select(value => (ActorTypeEnum)value), payload.RequiresLocalVerification,
            payload.AllowedCurrencyCodes, payload.DefaultCurrencyCode, payload.RefundProtectionIds.Select(value => (PaidEventRefundProtection)value),
            payload.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(limit.CurrencyCode,
                limit.PerEventSalesCeilingMinor, limit.PerEventSalesCountCeiling, limit.RollingOrganizerSalesCeilingMinor,
                limit.RollingOrganizerSalesCountCeiling, limit.RollingOrganizerWindowDays, limit.HighValueReviewThresholdMinor)),
            payload.RequiresFirstPaidEventReview, payload.FarFutureReviewThresholdDays);
        policy.CreatedAt = occurred; policy.CreatedBy = null;
        return policy;
    }

    private static ConfigurationManifestOperation Operation(ConfigurationManifestApplyPlan plan,
        ConfigurationManifestOperationStatus status, int created, int skipped, int failed, string? reason,
        IEnumerable<string> settings, IEnumerable<string> documents, DateTime completedAt) =>
        ConfigurationManifestOperation.Create(plan.OperationId, ConfigurationManifestAuditMode.Bootstrap,
            plan.ApiVersion, plan.Kind, plan.ManifestName, plan.Digest, status, plan.Tenants.Length,
            created, skipped, failed, reason, reason is null ? null : "The original manifest attempt was rejected.",
            plan.OccurredAt, completedAt, plan.InstanceSectionDigest, plan.BootstrapState?.Generation ?? 1, settings, documents);

    private static void Add(ConfigurationManifestBootstrapData data, JsonObject image, JsonArray changes, object entity)
    {
        var relation = data.Relations.Single(relation => relation.Type == entity.GetType());
        var row = data.Row(entity);
        var rows = ConfigurationManifestBootstrapData.Rows(image, relation.Tag);
        string key = ConfigurationManifestBootstrapData.Key(relation, row);
        var previous = rows.OfType<JsonObject>().SingleOrDefault(existing => ConfigurationManifestBootstrapData.Key(relation, existing) == key);
        if (previous is not null && JsonNode.DeepEquals(previous, row)) return;
        changes.Add(new JsonObject { ["Tag"] = relation.Tag, ["Before"] = previous?.DeepClone(), ["After"] = row.DeepClone() });
        if (previous is not null) rows.Remove(previous);
        rows.Add(row);
    }

    private static IEnumerable<JsonObject> Rows(JsonObject image, string tag) => image[tag]!.AsArray().OfType<JsonObject>();
    private static string? Text(JsonObject row, string name) => row[name]?.GetValue<string>();
    private static int Number(JsonObject row, string name) => row[name]!.GetValue<int>();
    private static int? NullableNumber(JsonObject row, string name) => row[name]?.GetValue<int>();
    private static long Long(JsonObject row, string name) => row[name]!.GetValue<long>();
    private static long? NullableLong(JsonObject row, string name) => row[name]?.GetValue<long>();
    private static bool Bool(JsonObject row, string name) => row[name]!.GetValue<bool>();
    private static Guid Id(JsonObject row, string name) => row[name]!.GetValue<Guid>();
    private static Guid? NullableId(JsonObject row, string name) => row[name]?.GetValue<Guid>();
    private static DateTime Time(JsonObject row, string name) => new(Long(row, name), DateTimeKind.Utc);
    private static DateTime? NullableTime(JsonObject row, string name) => row[name] is null ? null : Time(row, name);
    private static SystemSetting System(JsonObject row) => new()
    {
        Id = Id(row, "Id"),
        SettingKey = Text(row, "SettingKey")!,
        Value = Text(row, "Value")!,
        SettingValueTypeId = Number(row, "SettingValueTypeId"),
        IsLocked = Bool(row, "IsLocked"),
        AllowedValues = row["AllowedValues"] is JsonArray ? row["AllowedValues"]!.ToJsonString() : Text(row, "AllowedValues"),
        Description = Text(row, "Description"),
        Category = Text(row, "Category"),
        DisplayOrder = Number(row, "DisplayOrder"),
        CreatedAt = Time(row, "CreatedAt"),
        CreatedBy = NullableId(row, "CreatedBy"),
        UpdatedAt = NullableTime(row, "UpdatedAt"),
        UpdatedBy = NullableId(row, "UpdatedBy")
    };
    private static Guid PublicationId(string key) => PublicationPolicySettingKeys.All.ToList().IndexOf(key) switch
    {
        0 => SeedIds.SystemSettingEventReportingIntakeEnabledId,
        1 => SeedIds.SystemSettingRequireApprovalId,
        2 => SeedIds.SystemSettingUserSubmissionEnabledId,
        3 => SeedIds.SystemSettingOrgSubmissionEnabledId,
        4 => SeedIds.SystemSettingGroupSubmissionEnabledId,
        _ => throw new UnauthorizedAccessException("Unknown publication key.")
    };
}
