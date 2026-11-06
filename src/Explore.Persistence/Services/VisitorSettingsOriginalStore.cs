using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Visitor data custody on the original selected human connection. C# owns validation
/// and canonical transitions; provider primitives only read and persist fixed rows.
/// </summary>
public sealed class VisitorSettingsOriginalStore(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock)
    : IVisitorSettingsOriginalStore, IDisposable
{
    private VisitorSettingsOriginal? original;
    private IVisitorSettingsOriginalSource? source;
    private ExploreDbContextExecutionLease? selection;
    private RelationalConnectionIdentity? connection;
    private DbContextId contextId;
    private DbTransaction? transaction;
    private object? lifetime;
    private string? database;
    private string? dataSource;
    private int? backend;
    private string? commandImage;
    private JsonObject? expected;
    private ImmutableDictionary<string, string?>? deployment;
    private bool acquired;
    private bool applied;
    private bool refused;
    private bool closed;

    public async Task ObserveAsync(VisitorSettingsOriginal request, IVisitorSettingsOriginalSource selected,
        CancellationToken token)
    {
        if (original is not null || request.Effects.IsDefaultOrEmpty
            || request.OccurredAt.Kind != DateTimeKind.Utc || request.OccurredAt > clock.GetUtcNow().UtcDateTime
            || request.Effects.Any(effect => !VisitorAccessSettingMutationGuard.Handles(effect.Key)
                || effect.TenantId != request.TenantId || effect.UserId.HasValue)
            || request.Effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != request.Effects.Length)
            throw Denied();
        _ = RelationalProviderClassifier.Classify(context.Database);
        original = request;
        source = selected;
        selection = services.GetRequiredService<ExploreDbContextExecutionLease>();
        selection.RequireOriginalOwner(context);
        lifetime = selection.HumanLifetime;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw Denied();
        await context.Database.OpenConnectionAsync(token);
        database = connection.Database;
        dataSource = connection.DataSource;
        backend = connection.BackendProcessId;
        connection.StateChange += OnStateChange;
        commandImage = JsonSerializer.Serialize(request.Command, request.Command.GetType());
        deployment = CaptureDeployment();
        RequireOriginal(request);
        expected = await ReadAsync(token);
        RequireOriginal(request);
    }

    public async Task AcquireAsync(VisitorSettingsOriginal request, CancellationToken token)
    {
        RequireOriginal(request);
        if (acquired || transaction is not null) throw Denied();
        transaction = RequireTransaction();
        await RequireImageAsync(token);
        acquired = true;
        RequireOriginal(request);
    }

    public async Task<VisitorSettingsSnapshot> ReadSnapshotAsync(VisitorSettingsOriginal request, CancellationToken token)
    {
        RequireActive(request);
        if (applied || refused) throw Denied();
        await VerifyAsync(request, token);
        return new(Rows<SystemSetting>(), Rows<TenantSetting>(),
            expected!["AccountRequiredTenantIds"]!.Deserialize<ImmutableArray<Guid>>(),
            new ConfigurationBuilder().AddInMemoryCollection(deployment!).Build());
    }

    public async Task ApplyValidatedAsync(VisitorSettingsOriginal request, IVisitorAccessSettingsWriter validator,
        ImmutableArray<(object Entity, bool Removed)> changes, CancellationToken token)
    {
        RequireActive(request);
        if (applied || refused || changes.IsDefault
            || validator is not VisitorAccessSettingsWriter actual
            || !ReferenceEquals(validator, services.GetRequiredService<IVisitorAccessSettingsWriter>()))
            throw Denied();
        actual.RequireOriginalValidated(request);
        await VerifyAsync(request, token);
        var transitions = Compile();
        if (transitions.Count != changes.Length) throw Denied();
        foreach (var transition in transitions)
        {
            var candidates = changes.Where(change => change.Entity.GetType() == transition.Type
                && Snapshot(change.Entity)["Id"]!.GetValue<string>() == transition.Candidate["Id"]!.GetValue<string>()).ToArray();
            if (candidates.Length != 1 || candidates[0].Removed != (transition.After is null)
                || !JsonNode.DeepEquals(Snapshot(candidates[0].Entity), transition.Candidate))
                throw Denied();
        }
        foreach (var transition in transitions)
        {
            RequireActive(request);
            actual.RequireOriginalValidated(request);
            string? projectionKey = transition.Type == typeof(SystemSetting)
                ? transition.Candidate[nameof(SystemSetting.SettingKey)]!.GetValue<string>() : null;
            long? projectionRevision = projectionKey is not null
                && ManagedTenantProvisioningPolicyInventory.Keys.Contains(projectionKey, StringComparer.Ordinal)
                ? await ManagedTenantProvisioningPolicyProjection.RequireOriginalSourceAsync(context, projectionKey,
                    transition.Before is null ? null : Entity<SystemSetting>(transition.Before), token)
                : null;
            await VisitorSettingsStorage.WriteAsync(context, request.TenantId, transition.Type,
                transition.Before, transition.After, () =>
                {
                    RequireActive(request);
                    actual.RequireOriginalValidated(request);
                }, token);
            if (projectionRevision.HasValue && projectionKey is not null)
            {
                RequireActive(request);
                actual.RequireOriginalValidated(request);
                await ManagedTenantProvisioningPolicyProjection.RecordAsync(context, projectionKey,
                    transition.After is null ? null : Entity<SystemSetting>(transition.After), token, projectionRevision);
            }
            var rows = expected![transition.Type.Name]!.AsArray();
            if (transition.Before is not null)
                rows.Remove(rows.Single(row => row!["Id"]!.GetValue<string>()
                    == transition.Before["Id"]!.GetValue<string>()));
            if (transition.After is not null) rows.Add(transition.After.DeepClone());
            SortRows(transition.Type);
        }
        applied = true;
        await VerifyAsync(request, token);
    }

    public async Task RefuseAsync(VisitorSettingsOriginal request, CancellationToken token)
    {
        RequireActive(request);
        if (applied || refused) throw Denied();
        await VerifyAsync(request, token);
        refused = true;
    }

    public async Task VerifyAsync(VisitorSettingsOriginal request, CancellationToken token)
    {
        RequireAcquired(request);
        await RequireImageAsync(token);
        RequireAcquired(request);
    }

    public async Task RequireCompleteAsync(VisitorSettingsOriginal request, CancellationToken token)
    {
        RequireAcquired(request);
        if (!applied && !refused) throw Denied();
        await VerifyAsync(request, token);
    }

    public void Close(VisitorSettingsOriginal request)
    {
        if (original is not null && !ReferenceEquals(original, request)) throw Denied();
        Dispose();
    }

    public void Dispose()
    {
        closed = true;
        transaction = null;
        if (connection is not null) connection.StateChange -= OnStateChange;
    }

    private void OnStateChange(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) closed = true;
    }

    private IHumanVisitorSettingsPreparation Preparation =>
        services.GetRequiredService<IHumanSettingsMutationPreparation>() as IHumanVisitorSettingsPreparation
        ?? throw Denied();

    private void RequireOriginal(VisitorSettingsOriginal request)
    {
        if (closed || original is null || source is null || selection is null || connection is null
            || !ReferenceEquals(original, request)) throw Denied();
        selection.RequireOriginalOwner(context);
        Preparation.RequireVisitorSource(source);
        var purpose = request.TenantId.HasValue
            ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance;
        if (!ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || context.ContextId != contextId || !connection.Matches(context)
            || connection.State != ConnectionState.Open || connection.Database != database
            || connection.DataSource != dataSource || connection.BackendProcessId != backend
            || selection.SelectedTenantId != request.TenantId || context.TenantFilterTenantId != request.TenantId
            || selection.PolicyPurpose != PolicyMutationPurpose.Standard || selection.HumanScope?.Purpose != purpose
            || !ReferenceEquals(selection.HumanScope.Subject, request.Subject)
            || !ReferenceEquals(source.Subject, request.Subject)
            || lifetime is null || !ReferenceEquals(selection.HumanLifetime, lifetime)
            || context.HumanCommitContextLease != contextId
            || !ReferenceEquals(context.HumanCommitAuditSubject, request.Subject)
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || JsonSerializer.Serialize(request.Command, request.Command.GetType()) != commandImage
            || !CaptureDeployment().OrderBy(item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(deployment!.OrderBy(item => item.Key, StringComparer.Ordinal)))
            throw Denied();
    }

    private DbTransaction RequireTransaction()
    {
        RequireOriginal(original!);
        return context.Database.CurrentTransaction?.GetDbTransaction() is { } selected
            && selected.IsolationLevel == IsolationLevel.Serializable && connection!.Owns(selected)
            && context.HumanCommitAttemptOwnsTransaction && System.Transactions.Transaction.Current is null
            ? selected : throw Denied();
    }

    private void RequireAcquired(VisitorSettingsOriginal request)
    {
        RequireOriginal(request);
        if (!acquired || !ReferenceEquals(transaction, RequireTransaction())) throw Denied();
    }

    private void RequireActive(VisitorSettingsOriginal request)
    {
        RequireAcquired(request);
        if (!ReferenceEquals(Preparation.CurrentVisitorOriginal, request)
            || !ReferenceEquals(Preparation.CurrentVisitorStore, this)) throw Denied();
    }

    private async Task RequireImageAsync(CancellationToken token)
    {
        if (!JsonNode.DeepEquals(expected, await ReadAsync(token)))
            throw new UnauthorizedAccessException("The original visitor rows, absences or participation sources changed.");
    }

    private async Task<JsonObject> ReadAsync(CancellationToken token)
    {
        RequireStorageOwner();
        JsonObject image;
        if (context.Database.IsNpgsql())
            image = await PostgresVisitorSettingsStorage.ReadAsync(context, original!.TenantId, RequireStorageOwner, token);
        else
        {
            string[] keys = VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray();
            var systems = await context.SystemSettings.AsNoTracking()
                .Where(row => keys.Contains(row.SettingKey)).ToListAsync(token);
            var tenants = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead)
                .AsNoTracking().Where(row => keys.Contains(row.SettingKey)
                    && (!original!.TenantId.HasValue || row.TenantId == original.TenantId)).ToListAsync(token);
            var participation = context.EventParticipationConfigurations
                .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead).AsNoTracking();
            var events = context.Events
                .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead).AsNoTracking();
            var required = await (from row in participation
                join item in events on new { row.Id, row.TenantId } equals new { item.Id, item.TenantId }
                where !row.IsDeleted && !item.IsDeleted
                    && row.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
                    && (!original!.TenantId.HasValue || row.TenantId == original.TenantId)
                select row.TenantId).Distinct().ToListAsync(token);
            image = new JsonObject
            {
                [nameof(SystemSetting)] = new JsonArray(systems.Select(row => (JsonNode)Snapshot(row)).ToArray()),
                [nameof(TenantSetting)] = new JsonArray(tenants.Select(row => (JsonNode)Snapshot(row)).ToArray()),
                ["AccountRequiredTenantIds"] = JsonSerializer.SerializeToNode(required.Order())
            };
        }
        foreach (var type in new[] { typeof(SystemSetting), typeof(TenantSetting) })
        {
            foreach (var row in image[type.Name]!.AsArray().OfType<JsonObject>())
                foreach (var property in context.Model.FindEntityType(type)!.GetProperties())
                    row[property.Name] = ValueNode(row[property.Name]?.Deserialize(property.ClrType));
            image[type.Name] = new JsonArray(image[type.Name]!.AsArray().OfType<JsonObject>()
                .OrderBy(row => row["Id"]!.GetValue<string>(), StringComparer.Ordinal)
                .Select(row => row.DeepClone()).ToArray());
        }
        image["AccountRequiredTenantIds"] = JsonSerializer.SerializeToNode(
            image["AccountRequiredTenantIds"]!.Deserialize<Guid[]>()!.Order());
        RequireStorageOwner();
        return image;
    }

    private void RequireStorageOwner()
    {
        RequireOriginal(original!);
        if (transaction is null)
        {
            if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw Denied();
        }
        else if (!ReferenceEquals(transaction, RequireTransaction())) throw Denied();
    }

    private ImmutableArray<T> Rows<T>() where T : class =>
        expected![typeof(T).Name]!.AsArray().OfType<JsonObject>().Select(Entity<T>).ToImmutableArray();

    private T Entity<T>(JsonObject image) where T : class
    {
        object entity = typeof(T) == typeof(SystemSetting)
            ? new SystemSetting { SettingKey = "", Value = "" }
            : new TenantSetting { SettingKey = "", Value = "", Tenant = null! };
        foreach (var property in context.Model.FindEntityType(typeof(T))!.GetProperties())
            property.PropertyInfo!.SetValue(entity, image[property.Name]?.Deserialize(property.ClrType));
        return (T)entity;
    }

    private JsonObject Snapshot(object entity)
    {
        var image = new JsonObject();
        foreach (var property in context.Model.FindEntityType(entity.GetType())!.GetProperties())
            image[property.Name] = ValueNode(property.GetGetter().GetClrValue(entity));
        return image;
    }

    private static JsonNode? ValueNode(object? value) => JsonSerializer.SerializeToNode(
        value is DateTime time
            ? time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime()
            : value,
        value?.GetType() ?? typeof(object));

    private void SortRows(Type type) =>
        expected![type.Name] = new JsonArray(expected[type.Name]!.AsArray().OfType<JsonObject>()
            .OrderBy(row => row["Id"]!.GetValue<string>(), StringComparer.Ordinal).Select(row => row.DeepClone()).ToArray());

    private List<Transition> Compile()
    {
        var transitions = new List<Transition>();
        var request = original!;
        foreach (var effect in request.Effects)
        {
            var type = effect.TenantId.HasValue ? typeof(TenantSetting) : typeof(SystemSetting);
            var before = expected![type.Name]!.AsArray().OfType<JsonObject>().SingleOrDefault(row =>
                row["SettingKey"]!.GetValue<string>() == effect.Key
                && (!effect.TenantId.HasValue || row["TenantId"]!.GetValue<Guid>() == effect.TenantId));
            if (before is null && effect.Kind == HumanSettingsMutationKind.Remove) continue;
            if (before is null && effect.Kind == HumanSettingsMutationKind.SetLock) throw Denied();
            var definition = SettingRegistry.Get(effect.Key)!;
            object entity = before is not null
                ? type == typeof(SystemSetting) ? Entity<SystemSetting>(before) : Entity<TenantSetting>(before)
                : type == typeof(SystemSetting)
                    ? new SystemSetting
                    {
                        Id = request.NewIds[effect.Key], SettingKey = effect.Key, Value = definition.DefaultValue,
                        ValueType = definition.ValueType, Category = definition.Category, Description = definition.Description,
                        CreatedAt = request.OccurredAt, CreatedBy = request.AuditActor
                    }
                    : new TenantSetting
                    {
                        Id = request.NewIds[effect.Key], TenantId = effect.TenantId!.Value, Tenant = null!,
                        SettingKey = effect.Key, Value = definition.DefaultValue,
                        CreatedAt = request.OccurredAt, CreatedBy = request.AuditActor
                    };
            if (entity is SystemSetting system)
            {
                if (effect.Kind == HumanSettingsMutationKind.SetValue) system.Value = effect.Value!;
                if (effect.Kind == HumanSettingsMutationKind.SetLock) system.IsLocked = effect.IsLocked!.Value;
                system.UpdatedAt = request.OccurredAt;
                system.UpdatedBy = request.AuditActor;
            }
            else if (entity is TenantSetting tenant)
            {
                if (effect.Kind == HumanSettingsMutationKind.SetValue) tenant.Value = effect.Value!;
                if (effect.Kind == HumanSettingsMutationKind.SetLock) tenant.IsLocked = effect.IsLocked!.Value;
                tenant.UpdatedAt = request.OccurredAt;
                tenant.UpdatedBy = request.AuditActor;
            }
            var candidate = Snapshot(entity);
            transitions.Add(new(type, before?.DeepClone().AsObject(),
                effect.Kind == HumanSettingsMutationKind.Remove ? null : candidate.DeepClone().AsObject(), candidate));
        }
        return transitions;
    }

    private ImmutableDictionary<string, string?> CaptureDeployment()
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var values = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (string key in new[] { "Authentication:Provider", "AUTHENTICATION_PROVIDER", "Keycloak:Authority", "Keycloak:ClientId" })
            values[key] = configuration[key];
        foreach (var child in configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys").GetChildren())
            values[child.Path] = child.Value;
        return values.ToImmutable();
    }

    private sealed record Transition(Type Type, JsonObject? Before, JsonObject? After, JsonObject Candidate);
    private static UnauthorizedAccessException Denied() =>
        new("The original visitor command, subject and selected physical attempt are required.");
}
