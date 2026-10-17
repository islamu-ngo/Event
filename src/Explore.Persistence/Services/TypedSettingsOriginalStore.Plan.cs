using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

public sealed partial class TypedSettingsOriginalStore
{
    private static readonly ConditionalWeakTable<ExploreDbContext, TypedSettingsOriginalStore> Plans = new();
    private TenantPlanAssignmentSelection? planSelection;
    private string? planFailure;
    private bool planAuditWritten;
    private EmailDeliverySettingsWriter? planEmailWriter;
    private VisitorAccessSettingsWriter? planVisitorWriter;
    private ImmutableDictionary<string, string?>? planDeploymentValues;
    internal IConfiguration PlanDeployment { get; private set; } = null!;
    internal DateTime PlanOccurredAt => occurred;
    private bool PlanRequiresAudit => planFailure is null && (!effects.IsEmpty || planSelection!.HasStorageQuota);

    private void ObservePlan()
    {
        var command = original!.PlanAssignment!;
        var assignment = Rows<TenantPlanAssignment>().SingleOrDefault();
        var version = Rows<TenantPlanVersion>().SingleOrDefault();
        planFailure = assignment is null ? "tenant_plan_assignment_not_found"
            : assignment.TenantId != command.TenantId ? "tenant_plan_assignment_tenant_mismatch"
            : assignment.TenantPlanAssignmentStatusId != (int)Explore.Domain.Enums.TenantPlanAssignmentStatusEnum.Active ? "tenant_plan_assignment_not_active"
            : version is null ? "tenant_plan_version_not_found" : null;
        var settings = Rows<TenantPlanVersionSetting>();
        planSelection = new(
            settings.Where(row => !PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal)
                    && !EmailDeliverySettingKeys.Contains(row.SettingKey)
                    && !VisitorAccessSettingMutationGuard.Handles(row.SettingKey))
                .Select(row => row.SettingKey).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            settings.Any(row => EmailDeliverySettingKeys.Contains(row.SettingKey)),
            settings.Any(row => VisitorAccessSettingMutationGuard.Handles(row.SettingKey)),
            settings.Any(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal)),
            Rows<TenantPlanVersionQuota>().Any(row => row.QuotaKey == TenantPlanQuotaKeys.StorageBytes));
        planDeploymentValues = CapturePlanDeployment();
        PlanDeployment = new ConfigurationBuilder().AddInMemoryCollection(planDeploymentValues).Build();
    }

    public TenantPlanAssignmentSelection SelectPlan(TypedSettingsOriginal request)
    {
        RequireSelection(request);
        return planSelection ?? throw new UnauthorizedAccessException("The original plan was not observed.");
    }

    private ImmutableArray<TypedSettingsEffect> CompilePlan() => planFailure is not null ? []
        : Rows<TenantPlanVersionSetting>().Select(row => new TypedSettingsEffect(row.SettingKey, row.JsonValue,
            PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal) ? null : row.IsLocked)).ToImmutableArray();

    internal static TypedSettingsOriginalStore? CurrentPlan(ExploreDbContext context)
    {
        if (!Plans.TryGetValue(context, out var store)) return null;
        store.Require(store.original!);
        return store;
    }

    internal ImmutableArray<T> PlanRows<T>()
    {
        Require(original!);
        return Rows<T>();
    }

    private ImmutableDictionary<string, string?> CapturePlanDeployment()
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var values = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (string key in new[] { "Authentication:Provider", "AUTHENTICATION_PROVIDER", "Keycloak:Authority", "Keycloak:ClientId" })
            values.Add(key, configuration[key]);
        foreach (var child in configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys").GetChildren())
            values.Add(child.Path, child.Value);
        return values.ToImmutable();
    }

    private async Task RequirePlanAsync(CancellationToken token)
    {
        Require(original!);
        if (original!.PlanAssignment is null || planDeploymentValues is null
            || !planDeploymentValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(CapturePlanDeployment().OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("Original plan deployment inputs changed.");
        await VerifyAsync(original, token);
    }

    public async Task<TenantPlanAssignmentApplication> ApplyPlanAsync(TypedSettingsOriginal request, CancellationToken token)
    {
        try
        {
            await RequirePlanAsync(token);
            if (!ReferenceEquals(request, original) || request.PlanAssignment is null || invoked)
                throw new UnauthorizedAccessException("Only the original unconsumed plan can apply.");
            invoked = true;
            TenantPlanAssignmentApplication Failure(string code)
            {
                rejected = true;
                return new(BaseCommandResponse.Failure(code, code, [code], request.PlanAssignment.AssignmentId), []);
            }
            if (planFailure is not null) return Failure(planFailure);
            var quota = Rows<TenantPlanVersionQuota>().SingleOrDefault(row => row.QuotaKey == TenantPlanQuotaKeys.StorageBytes);
            var systems = Rows<SystemSetting>().ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
            if (quota is not null && systems.TryGetValue(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, out var ceiling)
                && TryPlanCeiling(ceiling.Value, out long limit) && quota.Limit > limit)
                return Failure("tenant_plan_quota_ceiling_exceeded");
            if (effects.Where(effect => planSelection!.OrdinaryKeys.Contains(effect.Key)
                    || EmailDeliverySettingKeys.Contains(effect.Key))
                .Any(effect => systems.GetValueOrDefault(effect.Key)?.IsLocked == true))
                return Failure("tenant_plan_setting_locked");
            if (!PlanRequiresAudit)
                return new(BaseCommandResponse.Success(request.PlanAssignment.AssignmentId, "Tenant plan applied."), []);

            if (RelationalProviderClassifier.Classify(context.Database) != RelationalProvider.PostgreSql)
                occurred = clock.GetUtcNow().UtcDateTime;
            var notices = ImmutableArray.CreateBuilder<SettingChangedNotification>();
            if (planSelection!.HasEmail)
            {
                var writer = services.GetRequiredService<EmailDeliverySettingsWriter>();
                planEmailWriter = writer;
                var result = await writer.ApplyPlanAsync(this, token);
                planEmailWriter = null;
                if (!result.IsAccepted())
                {
                    rejected = true;
                    return new(result.ToCommandResponse(request.PlanAssignment.AssignmentId, "Tenant plan applied."), []);
                }
                notices.AddRange(result.ToNotifications(request.Actor));
            }
            if (planSelection.HasVisitor)
            {
                var writer = (VisitorAccessSettingsWriter)services.GetRequiredService<IVisitorAccessSettingsWriter>();
                planVisitorWriter = writer;
                var result = await writer.ApplyPlanAsync(this, token);
                planVisitorWriter = null;
                if (!result.Success)
                    throw new TenantPlanOriginalRejectedException(Failure(result.FailureCode!).Response);
                notices.AddRange(result.DeferredNotifications);
            }
            var publicationEffects = PublicationPolicySettingKeys.All.Select(key => effects.SingleOrDefault(effect => effect.Key == key))
                .OfType<TypedSettingsEffect>().Select(effect => new PublicationPolicySettingMutation(effect.Key,
                    PublicationPolicyMutationKind.Set, effect.Value, request.TenantId, IsLocked: null)).ToImmutableArray();
            if (!publicationEffects.IsEmpty)
            {
                publication = new(request, this, new(
                    systems.Values.Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal))
                        .Select(row => new PublicationPolicySystemValueSnapshot(row.SettingKey, row.Value, row.IsLocked)).ToImmutableArray(),
                    Rows<TenantSetting>().Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal))
                        .Select(row => new PublicationPolicyTenantValueSnapshot(row.TenantId, row.SettingKey, row.Value)).ToImmutableArray()),
                    publicationEffects, occurred);
                var result = await services.GetRequiredService<ISettingMutationLock>().ExecuteManyAsync(
                    PublicationPolicySettingKeys.All,
                    inner => services.GetRequiredService<IPublicationPolicyMutationBoundary>()
                        .ApplyTypedInCurrentTransactionAsync(publication, inner), token);
                if (!result.Success)
                    throw new TenantPlanOriginalRejectedException(Failure(string.IsNullOrWhiteSpace(result.FailureCode)
                        ? "event_reporting_intake_policy_invalid" : result.FailureCode).Response);
                notices.AddRange(result.DeferredNotifications);
            }
            foreach (var effect in effects.Where(effect => planSelection.OrdinaryKeys.Contains(effect.Key))
                         .OrderBy(effect => effect.Key, StringComparer.Ordinal))
            {
                Explore.Domain.Ai.AiCredentialReference.RejectRetiredSettingMutation(effect.Key);
                EventResourceSettingMutationGuard.RejectGenericMutation(effect.Key);
                EventResourceProviderBindingDocument.RejectGenericMutation(effect.Key);
                InstanceOperatorIdentitySettingKeys.RejectGenericMutation(effect.Key);
                var change = await WriteAsync(effect, token);
                notices.Add(new(effect.Key, change!.OldValue, change.NewValue,
                    effect.IsLocked == true ? SettingSource.TenantLocked : SettingSource.TenantOverride,
                    request.TenantId, request.Actor, occurred));
            }
            await WritePlanAuditAsync(token);
            return new(BaseCommandResponse.Success(request.PlanAssignment.AssignmentId, "Tenant plan applied."), notices.ToImmutable());
        }
        catch { closed = true; throw; }
    }

    private static bool TryPlanCeiling(string value, out long limit)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)) return true;
        try
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.ValueKind == JsonValueKind.Number ? json.RootElement.TryGetInt64(out limit)
                : json.RootElement.ValueKind == JsonValueKind.String
                    && long.TryParse(json.RootElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit);
        }
        catch (JsonException) { return false; }
    }

    internal async Task RequirePlanEmailAsync(EmailDeliverySettingsWriter writer, CancellationToken token)
    {
        if (!ReferenceEquals(writer, planEmailWriter)
            || !ReferenceEquals(writer, services.GetRequiredService<EmailDeliverySettingsWriter>()))
            throw new UnauthorizedAccessException("The original joined plan Email writer is required.");
        await RequirePlanAsync(token);
    }

    internal ImmutableArray<EmailDeliverySettingMutation> PlanEmailMutations =>
        effects.Where(effect => EmailDeliverySettingKeys.Contains(effect.Key)).Select(effect =>
            new EmailDeliverySettingMutation(original!.TenantId, effect.Key, EmailDeliverySettingMutationKind.SetValue,
                effect.Value, effect.IsLocked)).ToImmutableArray();

    internal ImmutableArray<VisitorAccessSettingMutation> PlanVisitorMutations =>
        effects.Where(effect => VisitorAccessSettingMutationGuard.Handles(effect.Key)).Select(effect =>
            new VisitorAccessSettingMutation(original!.TenantId, effect.Key, VisitorAccessSettingMutationKind.SetValue,
                effect.Value, effect.IsLocked)).ToImmutableArray();

    internal Guid PlanActor => original!.Actor;
    internal Guid PlanTenant => original!.TenantId!.Value;
    internal HumanCommitSubject PlanSubject => source!.Subject;

    internal Dictionary<string, SystemSetting> PlanEmailSystems() =>
        PlanRows<SystemSetting>().Where(row => EmailDeliverySettingKeys.Contains(row.SettingKey))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);

    internal Dictionary<Guid, Dictionary<string, TenantSetting>> PlanEmailTenants() =>
        MixedSettingsDataImage.TenantIds(expected!.ToJsonString()).ToDictionary(tenant => tenant,
            tenant => PlanRows<TenantSetting>().Where(row => row.TenantId == tenant && EmailDeliverySettingKeys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal));

    internal EmailDeliveryPolicySet PlanEmailPolicies() =>
        EmailDeliveryPolicyReader.EvaluateAll(PlanEmailSystems(), PlanEmailTenants());

    internal List<Guid> PlanAccountRequiredTenants()
    {
        Require(original!);
        return expected![nameof(EventParticipationConfiguration)]!.AsArray().OfType<JsonObject>()
            .Where(row => !row[nameof(EventParticipationConfiguration.IsDeleted)]!.GetValue<bool>()
                && row[nameof(EventParticipationConfiguration.IdentityAccessModeId)]?.GetValue<int>()
                    == (int)Explore.Domain.Enums.IdentityAccessModeEnum.AccountRequired)
            .Select(row => Guid.Parse(row[nameof(EventParticipationConfiguration.TenantId)]!.GetValue<string>()))
            .Distinct().ToList();
    }

    internal async Task WritePlanEmailControlsAsync(IReadOnlyList<PostgresEmailDeliveryPolicyContract.ControlChange> changes,
        DateTime now, Guid? actor, CancellationToken token)
    {
        if (now != occurred || actor != PlanActor || changes.Count != 1 || changes[0].TenantId != PlanTenant)
            throw new UnauthorizedAccessException("The plan's original Email control is required.");
        var change = changes[0];
        var present = PlanRows<EmailDispatchTenantControl>().SingleOrDefault(row => row.TenantId == PlanTenant);
        if ((present?.DeliveryPolicyRevision ?? 0) != change.ExpectedRevision)
            throw new UnauthorizedAccessException("The original plan Email revision changed.");
        var row = present ?? new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlanTenant,
            CreatedAt = now,
            CreatedBy = actor
        };
        row.DeliveryPolicyRevision = change.Revision;
        row.OptionalSuppressedThroughRevision = change.SuppressedRevision;
        row.OptionalSuppressedThroughUtc = change.SuppressedUtc;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;
        await WritePlanSpecializedAsync([(row, present is null ? EntityState.Added : EntityState.Modified)], true, token);
    }

    internal async Task RequirePlanVisitorAsync(VisitorAccessSettingsWriter writer,
        ImmutableArray<VisitorAccessSettingMutation> mutations, CancellationToken token)
    {
        if (!ReferenceEquals(writer, planVisitorWriter)
            || !ReferenceEquals(writer, services.GetRequiredService<IVisitorAccessSettingsWriter>())
            || !mutations.SequenceEqual(PlanVisitorMutations))
            throw new UnauthorizedAccessException("The original joined plan visitor writer is required.");
        await RequirePlanAsync(token);
    }

    internal async Task WritePlanSpecializedAsync(ImmutableArray<(object Entity, EntityState State)> writes,
        bool email, CancellationToken token)
    {
        try
        {
            await RequirePlanAsync(token);
            if (email ? planEmailWriter is null : planVisitorWriter is null)
                throw new UnauthorizedAccessException("A real joined plan writer is required.");
            foreach (var (entity, state) in writes)
            {
                if (state is not (EntityState.Added or EntityState.Modified)
                    || entity is not (TenantSetting or EmailDispatchTenantControl)
                    || entity is EmailDispatchTenantControl && !email)
                    throw new UnauthorizedAccessException("The plan writer widened its original effects.");
                var type = entity.GetType();
                var after = Snapshot(entity, type);
                var rows = expected![type.Name]!.AsArray();
                var old = rows.OfType<JsonObject>().SingleOrDefault(row => row["Id"]!.ToString() == after["Id"]!.ToString());
                if (after["TenantId"]!.GetValue<string>() != PlanTenant.ToString("D")
                    || after["UpdatedBy"]?.GetValue<string>() != PlanActor.ToString("D")
                    || after["UpdatedAt"]?.GetValue<string>() != occurred.ToString("O", CultureInfo.InvariantCulture))
                    throw new UnauthorizedAccessException("Plan writer audit or tenant changed.");
                if (entity is TenantSetting setting)
                {
                    var effect = effects.Single(item => item.Key == setting.SettingKey);
                    if ((email ? !EmailDeliverySettingKeys.Contains(effect.Key) : !VisitorAccessSettingMutationGuard.Handles(effect.Key))
                        || setting.Value != effect.Value || setting.IsLocked != effect.IsLocked || !written.Add(effect.Key))
                        throw new UnauthorizedAccessException("The joined plan value or lock changed.");
                }
                await EmitPlanRowAsync(type, old, after, token);
            }
        }
        catch { closed = true; throw; }
    }

    internal async Task CompletePlanEmailAsync(EmailDeliverySettingsWriter writer, CancellationToken token)
    {
        await RequirePlanEmailAsync(writer, token);
        foreach (var effect in effects.Where(effect => EmailDeliverySettingKeys.Contains(effect.Key) && !written.Contains(effect.Key)))
        {
            var row = expected![nameof(TenantSetting)]!.AsArray().OfType<JsonObject>()
                .Single(item => item["SettingKey"]!.GetValue<string>() == effect.Key);
            if (row["Value"]!.GetValue<string>() != effect.Value || row["IsLocked"]!.GetValue<bool>() != effect.IsLocked)
                throw new UnauthorizedAccessException("The plan Email writer omitted an original value or lock.");
            await EmitPlanRowAsync(typeof(TenantSetting), row, row.DeepClone().AsObject(), token);
            written.Add(effect.Key);
        }
    }

    private async Task WritePlanAuditAsync(CancellationToken token)
    {
        await RequirePlanAsync(token);
        if (planAuditWritten || effects.Any(effect => !written.Contains(effect.Key)))
            throw new UnauthorizedAccessException("Plan audit requires every original group exactly once.");
        var old = expected![nameof(TenantPlanAssignment)]!.AsArray().OfType<JsonObject>().Single();
        var after = old.DeepClone().AsObject();
        after["UpdatedAt"] = occurred.ToString("O", CultureInfo.InvariantCulture);
        after["UpdatedBy"] = PlanActor.ToString("D");
        await EmitPlanRowAsync(typeof(TenantPlanAssignment), old, after, token);
        planAuditWritten = true;
    }

    private async Task EmitPlanRowAsync(Type type, JsonObject? old, JsonObject after, CancellationToken token)
    {
        Guid id = Guid.Parse(after["Id"]!.GetValue<string>());
        var change = new JsonObject
        {
            ["Type"] = type.Name,
            ["Id"] = id.ToString("D"),
            ["Before"] = old?.DeepClone(),
            ["After"] = after.DeepClone()
        };
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.WritePlanAsync(context, enrollment!, original!, new JsonArray(change).ToJsonString(), token);
        else
            await TypedSettingsSqliteWriter.WriteAsync(context, transaction, type, id, old, after, token);
        var rows = expected![type.Name]!.AsArray();
        if (old is not null) rows.Remove(old);
        rows.Add(after);
        expected[type.Name] = new JsonArray(rows.OfType<JsonObject>().OrderBy(row => row["Id"]!.ToString(), StringComparer.Ordinal)
            .Select(row => row.DeepClone()).ToArray());
        await source!.RequireSourcesAsync(token);
        source.RequireTime(clock.GetUtcNow());
    }
}
