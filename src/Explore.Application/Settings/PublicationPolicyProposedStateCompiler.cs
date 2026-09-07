namespace Explore.Application.Settings;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Settings;

public static class PublicationPolicyProposedStateCompiler
{
    private const string InvalidPolicyCode = "event_reporting_intake_policy_invalid";

    public static PublicationPolicyCompilationResult CompileTenant(
        PublicationPolicyTenantCompilationInput input)
    {
        if (input is null
            || input.TenantId is not Guid tenantId
            || tenantId == Guid.Empty
            || !TryReadDefaults(out Dictionary<string, bool>? defaults)
            || !TryReadSystemValues(input.SystemValues, out Dictionary<string, SystemValue>? systems)
            || !TryReadTenantValues(input.TenantValues, tenantId, out Dictionary<TenantValueKey, bool>? tenants)
            || !ValidateTenantMutations(input.Mutations, tenantId))
        {
            return Invalid();
        }

        foreach (PublicationPolicySettingMutation mutation in input.Mutations)
        {
            var key = new TenantValueKey(tenantId, mutation.Key);
            if (mutation.Kind == PublicationPolicyMutationKind.Set)
            {
                if (!TryParseBoolean(mutation.JsonValue, out bool value))
                    return Invalid();

                tenants[key] = value;
            }
            else
            {
                tenants.Remove(key);
            }
        }

        ReportingIntakePolicyState state = CompileState(tenantId, defaults, systems, tenants);
        return new PublicationPolicyCompilationResult(
            Success: true,
            FailureCode: null,
            BaseTenantState: null,
            TenantStates: [new PublicationPolicyCompiledTenantState(tenantId, state)]);
    }

    public static PublicationPolicyCompilationResult CompileInstance(
        PublicationPolicyInstanceCompilationInput input)
    {
        if (input is null
            || !TryReadDefaults(out Dictionary<string, bool>? defaults)
            || !TryReadSystemValues(input.SystemValues, out Dictionary<string, SystemValue>? systems)
            || !TryReadTenantValues(input.TenantValues, expectedTenantId: null, out Dictionary<TenantValueKey, bool>? tenants)
            || !ValidateSystemMutations(input.Mutations))
        {
            return Invalid();
        }

        foreach (PublicationPolicySettingMutation mutation in input.Mutations)
        {
            if (mutation.Kind == PublicationPolicyMutationKind.Set)
            {
                if (!TryParseBoolean(mutation.JsonValue, out bool value)
                    || mutation.IsLocked is not bool isLocked)
                {
                    return Invalid();
                }

                systems[mutation.Key] = new SystemValue(value, isLocked);
            }
            else
            {
                systems.Remove(mutation.Key);
            }
        }

        ReportingIntakePolicyState baseState = CompileState(
            tenantId: null,
            defaults,
            systems,
            tenants);
        ImmutableArray<PublicationPolicyCompiledTenantState> tenantStates = tenants.Keys
            .Select(key => key.TenantId)
            .Distinct()
            .OrderBy(tenantId => tenantId)
            .Select(tenantId => new PublicationPolicyCompiledTenantState(
                tenantId,
                CompileState(tenantId, defaults, systems, tenants)))
            .ToImmutableArray();

        return new PublicationPolicyCompilationResult(
            Success: true,
            FailureCode: null,
            BaseTenantState: baseState,
            TenantStates: tenantStates);
    }

    private static bool TryReadDefaults(out Dictionary<string, bool> defaults)
    {
        defaults = new Dictionary<string, bool>(PublicationPolicySettingKeys.All.Count, StringComparer.Ordinal);
        foreach (string key in PublicationPolicySettingKeys.All)
        {
            SettingDefinition? definition = SettingRegistry.Get(key);
            if (definition is null
                || !definition.RequiresCoordinatedMutation
                || definition.ValueType != SettingValueType.Boolean
                || !TryParseBoolean(definition.DefaultValue, out bool value))
            {
                defaults.Clear();
                return false;
            }

            defaults.Add(key, value);
        }

        return true;
    }

    private static bool TryReadSystemValues(
        ImmutableArray<PublicationPolicySystemValueSnapshot> snapshots,
        out Dictionary<string, SystemValue> systems)
    {
        systems = new Dictionary<string, SystemValue>(StringComparer.Ordinal);
        if (snapshots.IsDefault)
            return false;

        foreach (PublicationPolicySystemValueSnapshot snapshot in snapshots)
        {
            if (snapshot is null
                || !IsGuardedKey(snapshot.Key)
                || systems.ContainsKey(snapshot.Key)
                || !TryParseBoolean(snapshot.JsonValue, out bool value))
            {
                systems.Clear();
                return false;
            }

            systems.Add(snapshot.Key, new SystemValue(value, snapshot.IsLocked));
        }

        return true;
    }

    private static bool TryReadTenantValues(
        ImmutableArray<PublicationPolicyTenantValueSnapshot> snapshots,
        Guid? expectedTenantId,
        out Dictionary<TenantValueKey, bool> tenants)
    {
        tenants = [];
        if (snapshots.IsDefault)
            return false;

        foreach (PublicationPolicyTenantValueSnapshot snapshot in snapshots)
        {
            if (snapshot is null
                || snapshot.TenantId is not Guid tenantId
                || tenantId == Guid.Empty
                || expectedTenantId is Guid expected && tenantId != expected
                || !IsGuardedKey(snapshot.Key)
                || !TryParseBoolean(snapshot.JsonValue, out bool value))
            {
                tenants.Clear();
                return false;
            }

            var key = new TenantValueKey(tenantId, snapshot.Key);
            if (!tenants.TryAdd(key, value))
            {
                tenants.Clear();
                return false;
            }
        }

        return true;
    }

    private static bool ValidateTenantMutations(
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid tenantId)
    {
        if (mutations.IsDefault)
            return false;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PublicationPolicySettingMutation mutation in mutations)
        {
            if (mutation is null
                || mutation.TenantId != tenantId
                || !IsGuardedKey(mutation.Key)
                || !keys.Add(mutation.Key)
                || !HasValidTenantShape(mutation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateSystemMutations(
        ImmutableArray<PublicationPolicySettingMutation> mutations)
    {
        if (mutations.IsDefault)
            return false;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PublicationPolicySettingMutation mutation in mutations)
        {
            if (mutation is null
                || mutation.TenantId is not null
                || !IsGuardedKey(mutation.Key)
                || !keys.Add(mutation.Key)
                || !HasValidSystemShape(mutation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidTenantShape(PublicationPolicySettingMutation mutation) =>
        mutation.Kind switch
        {
            PublicationPolicyMutationKind.Set =>
                mutation.IsLocked is null && TryParseBoolean(mutation.JsonValue, out _),
            PublicationPolicyMutationKind.Remove =>
                mutation.JsonValue is null && mutation.IsLocked is null,
            _ => false
        };

    private static bool HasValidSystemShape(PublicationPolicySettingMutation mutation) =>
        mutation.Kind switch
        {
            PublicationPolicyMutationKind.Set =>
                mutation.IsLocked is not null && TryParseBoolean(mutation.JsonValue, out _),
            PublicationPolicyMutationKind.Remove =>
                mutation.JsonValue is null && mutation.IsLocked is null,
            _ => false
        };

    private static bool IsGuardedKey(string? key)
    {
        if (key is null || !PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
            return false;

        SettingDefinition? definition = SettingRegistry.Get(key);
        return definition is
        {
            RequiresCoordinatedMutation: true,
            ValueType: SettingValueType.Boolean
        };
    }

    private static ReportingIntakePolicyState CompileState(
        Guid? tenantId,
        IReadOnlyDictionary<string, bool> defaults,
        IReadOnlyDictionary<string, SystemValue> systems,
        IReadOnlyDictionary<TenantValueKey, bool> tenants) =>
        new(
            Resolve(PublicationPolicySettingKeys.All[0], tenantId, defaults, systems, tenants),
            Resolve(PublicationPolicySettingKeys.All[1], tenantId, defaults, systems, tenants),
            Resolve(PublicationPolicySettingKeys.All[2], tenantId, defaults, systems, tenants),
            Resolve(PublicationPolicySettingKeys.All[3], tenantId, defaults, systems, tenants),
            Resolve(PublicationPolicySettingKeys.All[4], tenantId, defaults, systems, tenants));

    private static bool Resolve(
        string key,
        Guid? tenantId,
        IReadOnlyDictionary<string, bool> defaults,
        IReadOnlyDictionary<string, SystemValue> systems,
        IReadOnlyDictionary<TenantValueKey, bool> tenants)
    {
        bool inheritedValue = defaults[key];
        if (systems.TryGetValue(key, out SystemValue system))
        {
            inheritedValue = system.Value;
            if (system.IsLocked)
                return inheritedValue;
        }

        return tenantId is Guid id && tenants.TryGetValue(new TenantValueKey(id, key), out bool tenantValue)
            ? tenantValue
            : inheritedValue;
    }

    private static bool TryParseBoolean(string? jsonValue, out bool value)
    {
        value = false;
        if (jsonValue is null)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonValue);
            if (document.RootElement.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            return document.RootElement.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static PublicationPolicyCompilationResult Invalid() => new(
        Success: false,
        FailureCode: InvalidPolicyCode,
        BaseTenantState: null,
        TenantStates: []);

    private readonly record struct SystemValue(bool Value, bool IsLocked);

    private readonly record struct TenantValueKey(Guid TenantId, string Key);
}
