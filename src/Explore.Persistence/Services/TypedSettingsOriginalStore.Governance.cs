using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

public sealed partial class TypedSettingsOriginalStore
{
    private readonly List<GovernanceProjection> governanceProjections = [];
    private readonly List<GovernanceEmailRevision> governanceEmailRevisions = [];
    private EmailDeliverySettingsWriter? governanceEmailWriter;
    private bool governanceEmailCompleted;
    private bool governanceCacheInvalidated;
    private static readonly JsonSerializerOptions GovernanceJson = new(JsonSerializerDefaults.Web);

    private sealed class GovernanceProjection(Type type, JsonObject? before, JsonObject after)
    {
        internal Type Type { get; } = type;
        internal JsonObject? Before { get; } = before;
        internal JsonObject After { get; } = after;
        internal bool Written { get; set; }
    }

    private sealed class GovernanceEmailRevision(Guid? tenant, long revision, long? suppressed, DateTime? cutoff)
    {
        internal Guid? Tenant { get; } = tenant;
        internal long Revision { get; } = revision;
        internal long? Suppressed { get; } = suppressed;
        internal DateTime? Cutoff { get; } = cutoff;
        internal bool Written { get; set; }
    }

    private void CompileGovernanceEmailRevisions()
    {
        var effect = effects.SingleOrDefault(item => item.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp);
        if (effect is null) return;
        var systems = Rows<SystemSetting>().Where(row => EmailDeliverySettingKeys.Contains(row.SettingKey))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenants = MixedSettingsDataImage.TenantIds(expected!.ToJsonString()).ToDictionary(tenant => tenant,
            tenant => Rows<TenantSetting>().Where(row => row.TenantId == tenant && EmailDeliverySettingKeys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal));
        var beforePolicies = EmailDeliveryPolicyReader.EvaluateAll(systems, tenants);
        systems.TryGetValue(effect.Key, out var setting);
        if (setting is not null && setting.Value == effect.Value && setting.IsLocked == effect.IsLocked) return;
        setting ??= new SystemSetting
        {
            SettingKey = effect.Key,
            Value = effect.Value!,
            ValueType = Explore.Domain.Settings.SettingRegistry.Get(effect.Key)!.ValueType
        };
        setting.Value = effect.Value!;
        setting.IsLocked = effect.IsLocked!.Value;
        systems[effect.Key] = setting;
        var afterPolicies = EmailDeliveryPolicyReader.EvaluateAll(systems, tenants);
        void Add(Guid? tenant, Explore.Application.Models.EmailDeliveryPolicySnapshot beforePolicy,
            Explore.Application.Models.EmailDeliveryPolicySnapshot afterPolicy, long revision, long? suppressed, DateTime? cutoff)
        {
            long next = checked(revision + 1);
            bool unavailable = afterPolicy.State != EmailDeliveryState.Available;
            bool restored = !unavailable && beforePolicy.State != EmailDeliveryState.Available;
            governanceEmailRevisions.Add(new(tenant, next,
                unavailable ? next : restored ? revision : suppressed,
                (unavailable || restored) && (!cutoff.HasValue || cutoff.Value < occurred) ? occurred : cutoff));
        }
        var processor = Rows<EmailDispatchProcessorState>().SingleOrDefault(row =>
            row.ProcessorCode == Explore.Persistence.Repositories.EmailDispatchOutboxRepository.SmtpProcessorCode);
        Add(null, beforePolicies.Instance, afterPolicies.Instance, processor?.DeliveryPolicyRevision ?? 0,
            processor?.OptionalSuppressedThroughRevision, processor?.OptionalSuppressedThroughUtc);
        foreach (var (tenant, current) in afterPolicies.Tenants)
        {
            var previous = beforePolicies.Tenants[tenant];
            if (previous.TransportTenantId is not null && current.TransportTenantId is not null
                && previous.Enabled == current.Enabled && previous.State == current.State) continue;
            var control = Rows<EmailDispatchTenantControl>().SingleOrDefault(row => row.TenantId == tenant);
            Add(tenant, previous, current, control?.DeliveryPolicyRevision ?? 0,
                control?.OptionalSuppressedThroughRevision, control?.OptionalSuppressedThroughUtc);
        }
    }

    private void CompileGovernanceProjections()
    {
        var governance = original!.Governance!;
        var bootstrap = expected![nameof(InstanceBootstrapState)]!.AsArray().OfType<JsonObject>()
            .OrderByDescending(row => row[nameof(InstanceBootstrapState.Generation)]!.GetValue<long>())
            .ThenByDescending(row => row[nameof(InstanceBootstrapState.CreatedAt)]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenByDescending(row => row["Id"]!.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault();
        bool singleTenant = bootstrap is null
            || bootstrap[nameof(InstanceBootstrapState.Status)]!.GetValue<int>() != (int)InstanceBootstrapStatus.Completed
            || bootstrap[nameof(InstanceBootstrapState.DeploymentMode)]!.GetValue<int>() == (int)DeploymentMode.SingleTenant;
        if (!singleTenant) return;
        if (governance.Modules is { Patch: var modules })
        {
            foreach (var (moduleKey, input) in new[]
            {
                ("Mod_Islamic", modules.EnableIslamicModule), ("Mod_Tech", modules.EnableTechModule)
            })
            {
                if (!input.HasValue) continue;
                var definition = Rows<ModuleDefinition>().SingleOrDefault(row => row.ModuleKey == moduleKey);
                if (definition is null) continue;
                var present = Rows<TenantCapability>().SingleOrDefault(row =>
                    row.TenantId == PlatformDefaults.DefaultTenantId && row.ModuleId == definition.Id);
                var old = present is null ? null : Snapshot(present, typeof(TenantCapability));
                var capability = present ?? new TenantCapability
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    ModuleId = definition.Id,
                    Module = null!,
                    EnabledAt = occurred,
                    EnabledBy = original.Actor,
                    CreatedAt = occurred,
                    CreatedBy = original.Actor
                };
                capability.IsEnabled = input.Value;
                if (input.Value && capability.EnabledAt == default)
                {
                    capability.EnabledAt = occurred;
                    capability.EnabledBy = original.Actor;
                }
                if (present is not null)
                {
                    capability.UpdatedAt = occurred;
                    capability.UpdatedBy = original.Actor;
                }
                governanceProjections.Add(new(typeof(TenantCapability), old, Snapshot(capability, typeof(TenantCapability))));
            }
        }
        if (governance.Branding is { Patch.DefaultBrandDisplayName.HasValue: true })
        {
            string displayName = JsonSerializer.Deserialize<string>(effects.Single(effect =>
                effect.Key == GovernanceSettingKeys.Branding.DisplayName).Value!)!;
            var document = Rows<TenantSettingsDocument>().SingleOrDefault();
            var old = document is null ? null : Snapshot(document, typeof(TenantSettingsDocument));
            if (document is null)
            {
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = expected[nameof(Tenant)]!.AsArray().OfType<JsonObject>()
                        .SingleOrDefault(row => row["Id"]!.GetValue<string>() == PlatformDefaults.DefaultTenantId.ToString("D"))
                        ?[nameof(Tenant.FullName)]?.GetValue<string>() ?? string.Empty;
                document = TenantBrandingSettingsDocumentDefaults.Create(PlatformDefaults.DefaultTenantId, displayName);
                document.Id = Guid.CreateVersion7();
                document.ConcurrencyStamp = Guid.CreateVersion7();
                document.CreatedAt = occurred;
                document.CreatedBy = original.Actor;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(displayName)) return;
                BrandingSettings payload;
                try { payload = JsonSerializer.Deserialize<BrandingSettings>(document.PayloadJson, GovernanceJson) ?? new(); }
                catch (JsonException) { payload = new(); }
                if (string.Equals(payload.DisplayName, displayName.Trim(), StringComparison.Ordinal)) return;
                document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion,
                    JsonSerializer.Serialize(payload with { DisplayName = displayName.Trim() }, GovernanceJson));
                document.ConcurrencyStamp = Guid.CreateVersion7();
                document.UpdatedAt = occurred;
                document.UpdatedBy = original.Actor;
            }
            governanceProjections.Add(new(typeof(TenantSettingsDocument), old, Snapshot(document, typeof(TenantSettingsDocument))));
        }
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyGovernanceAsync(
        TypedSettingsOriginal request, CancellationToken token)
    {
        try
        {
            Require(request);
            if (request.Governance is null || invoked)
                throw new UnauthorizedAccessException("The exact unconsumed governance original is required.");
            invoked = true;
            var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
            if (effects.Any(effect => EmailDeliverySettingKeys.Contains(effect.Key)))
            {
                governanceEmailWriter = services.GetRequiredService<EmailDeliverySettingsWriter>();
                var result = await governanceEmailWriter.ApplyGovernanceAsync(this, token);
                governanceEmailWriter = null;
                // A declined specialized write aborts the whole PATCH, including earlier
                // revisions and projections. It cannot be converted into partial success.
                result.EnsureAccepted();
                notifications.AddRange(result.ToNotifications(request.Actor));
            }
            foreach (var effect in effects.Where(effect => !EmailDeliverySettingKeys.Contains(effect.Key)))
            {
                var change = await WriteAsync(effect, token);
                if (change is not null)
                    notifications.Add(new(effect.Key, change.OldValue, change.NewValue,
                        effect.IsLocked == true ? SettingSource.SystemLocked : SettingSource.SystemDefault,
                        null, request.Actor, occurred));
            }
            foreach (var projection in governanceProjections)
            {
                await VerifyAsync(request, token);
                if (projection.Type == typeof(TenantCapability))
                    context.DisclosureMutations.Enlist([PlatformDefaults.DefaultTenantId]);
                await EmitGovernanceRowAsync(projection.Type, projection.Before, projection.After, token);
                projection.Written = true;
            }
            return notifications.ToImmutable();
        }
        catch { closed = true; throw; }
    }

    public void InvalidateGovernanceAfterCommit(TypedSettingsOriginal request)
    {
        if (!ReferenceEquals(request, original) || request.Governance is null || !closed || !completed || rejected
            || governanceCacheInvalidated || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Governance cache publication requires the settled original writer.");
        governanceCacheInvalidated = true;
        if (governanceProjections.Any(projection => projection.Type == typeof(TenantSettingsDocument)))
            services.GetRequiredService<ITypedSettingsDocumentResolver>().InvalidateTenantDocumentCache(
                PlatformDefaults.DefaultTenantId, SettingsDocumentKeys.Tenant.Branding);
    }

    internal async Task RequireGovernanceEmailAsync(EmailDeliverySettingsWriter writer, CancellationToken token)
    {
        Require(original!);
        if (original!.Governance?.Delegation?.Patch.LockTenantSmtp.HasValue != true
            || !ReferenceEquals(writer, governanceEmailWriter)
            || !ReferenceEquals(writer, services.GetRequiredService<EmailDeliverySettingsWriter>()))
            throw new UnauthorizedAccessException("The original joined governance SMTP writer is required.");
        await VerifyAsync(original, token);
    }

    internal ImmutableArray<EmailDeliverySettingMutation> GovernanceEmailMutations =>
        effects.Where(effect => EmailDeliverySettingKeys.Contains(effect.Key)).Select(effect =>
            new EmailDeliverySettingMutation(null, effect.Key, EmailDeliverySettingMutationKind.SetValue, effect.Value, effect.IsLocked))
            .ToImmutableArray();
    internal Guid GovernanceActor => original!.Actor;
    internal DateTime GovernanceOccurredAt => occurred;
    internal ImmutableArray<T> GovernanceRows<T>() { Require(original!); return Rows<T>(); }
    internal Dictionary<string, SystemSetting> GovernanceEmailSystems() =>
        GovernanceRows<SystemSetting>().Where(row => EmailDeliverySettingKeys.Contains(row.SettingKey))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
    internal Dictionary<Guid, Dictionary<string, TenantSetting>> GovernanceEmailTenants() =>
        MixedSettingsDataImage.TenantIds(expected!.ToJsonString()).ToDictionary(tenant => tenant,
            tenant => GovernanceRows<TenantSetting>().Where(row => row.TenantId == tenant && EmailDeliverySettingKeys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal));
    internal EmailDeliveryPolicySet GovernanceEmailPolicies() =>
        EmailDeliveryPolicyReader.EvaluateAll(GovernanceEmailSystems(), GovernanceEmailTenants());

    internal async Task WriteGovernanceEmailRowsAsync(
        ImmutableArray<(object Entity, EntityState State)> rows, CancellationToken token)
    {
        await RequireGovernanceEmailAsync(governanceEmailWriter
            ?? throw new UnauthorizedAccessException("The joined SMTP writer is absent."), token);
        foreach (var (entity, state) in rows)
        {
            if (state is not (EntityState.Added or EntityState.Modified)
                || entity is not (SystemSetting or EmailDispatchProcessorState or EmailDispatchTenantControl))
                throw new UnauthorizedAccessException("The governance SMTP writer widened its projection.");
            var type = entity.GetType();
            var after = Snapshot(entity, type);
            var old = expected![type.Name]!.AsArray().OfType<JsonObject>().SingleOrDefault(row =>
                row["Id"]!.GetValue<string>() == after["Id"]!.GetValue<string>());
            if (entity is SystemSetting setting)
            {
                var effect = effects.Single(item => item.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp);
                if (setting.SettingKey != effect.Key || setting.Value != effect.Value
                    || setting.IsLocked != effect.IsLocked || !written.Add(effect.Key))
                    throw new UnauthorizedAccessException("The original SMTP delegation value or lock changed.");
            }
            else
            {
                Guid? tenant = entity is EmailDispatchTenantControl control ? control.TenantId : null;
                var compiled = governanceEmailRevisions.SingleOrDefault(revision => revision.Tenant == tenant);
                if (compiled is null || compiled.Written
                    || after["DeliveryPolicyRevision"]!.GetValue<long>() != compiled.Revision
                    || after["OptionalSuppressedThroughRevision"]?.GetValue<long>() != compiled.Suppressed
                    || after["OptionalSuppressedThroughUtc"]?.GetValue<string>()
                        != compiled.Cutoff?.ToString("O", CultureInfo.InvariantCulture)
                    || after["UpdatedAt"]?.GetValue<string>() != occurred.ToString("O", CultureInfo.InvariantCulture)
                    || after["UpdatedBy"]?.GetValue<string>() != original!.Actor.ToString("D"))
                    throw new UnauthorizedAccessException("The SMTP revision differs from its acquired original compilation.");
                if (old is not null)
                {
                    var unchangedBefore = old.DeepClone().AsObject();
                    var unchangedAfter = after.DeepClone().AsObject();
                    foreach (string property in new[] { "DeliveryPolicyRevision", "OptionalSuppressedThroughRevision",
                        "OptionalSuppressedThroughUtc", "UpdatedAt", "UpdatedBy" })
                    {
                        unchangedBefore.Remove(property);
                        unchangedAfter.Remove(property);
                    }
                    if (!JsonNode.DeepEquals(unchangedBefore, unchangedAfter))
                        throw new UnauthorizedAccessException("The SMTP revision changed original metadata.");
                }
                compiled.Written = true;
            }
            await EmitGovernanceRowAsync(type, old, after, token);
        }
    }

    internal async Task WriteGovernanceEmailControlsAsync(
        IReadOnlyList<PostgresEmailDeliveryPolicyContract.ControlChange> changes,
        DateTime now, Guid? actor, CancellationToken token)
    {
        if (now != occurred || actor != original!.Actor)
            throw new UnauthorizedAccessException("The original governance SMTP audit is required.");
        foreach (var change in changes)
        {
            var present = GovernanceRows<EmailDispatchTenantControl>().SingleOrDefault(row => row.TenantId == change.TenantId);
            if ((present?.DeliveryPolicyRevision ?? 0) != change.ExpectedRevision)
                throw new UnauthorizedAccessException("The original governance SMTP revision changed.");
            var row = present ?? new EmailDispatchTenantControl
            {
                Id = Guid.CreateVersion7(),
                TenantId = change.TenantId,
                CreatedAt = now,
                CreatedBy = actor
            };
            row.DeliveryPolicyRevision = change.Revision;
            row.OptionalSuppressedThroughRevision = change.SuppressedRevision;
            row.OptionalSuppressedThroughUtc = change.SuppressedUtc;
            row.UpdatedAt = now;
            row.UpdatedBy = actor;
            await WriteGovernanceEmailRowsAsync([(row, present is null ? EntityState.Added : EntityState.Modified)], token);
        }
    }

    internal async Task CompleteGovernanceEmailAsync(EmailDeliverySettingsWriter writer, CancellationToken token)
    {
        await RequireGovernanceEmailAsync(writer, token);
        foreach (var effect in effects.Where(effect => EmailDeliverySettingKeys.Contains(effect.Key) && !written.Contains(effect.Key)))
        {
            var row = expected![nameof(SystemSetting)]!.AsArray().OfType<JsonObject>().Single(item =>
                item[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == effect.Key);
            if (row[nameof(SystemSetting.Value)]!.GetValue<string>() != effect.Value
                || row[nameof(SystemSetting.IsLocked)]!.GetValue<bool>() != effect.IsLocked)
                throw new UnauthorizedAccessException("The original SMTP delegation effect was omitted.");
            await EmitGovernanceRowAsync(typeof(SystemSetting), row, row.DeepClone().AsObject(), token);
            written.Add(effect.Key);
        }
        governanceEmailCompleted = true;
    }

    private async Task EmitGovernanceRowAsync(Type type, JsonObject? old, JsonObject after, CancellationToken token)
    {
        Guid id = Guid.Parse(after["Id"]!.GetValue<string>());
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.WriteGovernanceAsync(context, enrollment!, original!, new JsonArray(new JsonObject
            {
                ["Type"] = type.Name,
                ["Id"] = id.ToString("D"),
                ["Before"] = old?.DeepClone(),
                ["After"] = after.DeepClone()
            }).ToJsonString(), token);
        else
            await TypedSettingsSqliteWriter.WriteAsync(context, transaction, type, id, old, after, token);
        var rows = expected![type.Name]!.AsArray();
        var present = rows.OfType<JsonObject>().SingleOrDefault(row => row["Id"]!.GetValue<string>() == id.ToString("D"));
        if (present is not null) rows.Remove(present);
        rows.Add(after.DeepClone());
        expected[type.Name] = new JsonArray(rows.OfType<JsonObject>().OrderBy(row => row["Id"]!.ToString(), StringComparer.Ordinal)
            .Select(row => row.DeepClone()).ToArray());
        await source!.RequireSourcesAsync(token);
        source.RequireTime(clock.GetUtcNow());
    }
}
