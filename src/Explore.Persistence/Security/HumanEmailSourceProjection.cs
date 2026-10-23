using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;

namespace Explore.Persistence.Security;

/// <summary>Decodes the fixed complete native payload; omitted sources never become empty defaults.</summary>
internal static class HumanEmailSourceProjection
{
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal static HumanPrincipalSourceFacts ReadPrincipal(JsonElement p, HumanCommitSubject subject,
        Guid? tenant, bool local)
    {
        ImmutableArray<T> Rows<T>(int index) => p.GetProperty($"q{index}").Deserialize<ImmutableArray<T>>(Json);
        HumanPrincipalLocalSessionFacts? credential = null;
        if (local)
        {
            ImmutableArray<T> Local<T>(int index) => p.GetProperty($"l{index}").Deserialize<ImmutableArray<T>>(Json);
            var accounts = Local<HumanPrincipalLocalAccountFact>(0);
            string? metadata = Local<StringValue>(1).SingleOrDefault()?.Value;
            LocalCredentialStateMetadata? state;
            try { state = string.IsNullOrWhiteSpace(metadata) ? null : JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadata, Json); }
            catch (JsonException) { state = null; }
            catch (ArgumentException) { state = null; }
            // Require the delivery contributor even if the confirmed account does not consume it.
            var deliveryRows = Local<StringValue>(4);
            string? delivery = accounts is [var account] && !account.EmailConfirmed
                ? deliveryRows.SingleOrDefault()?.Value : null;
            credential = new(accounts, state, Local<HumanPrincipalLocalOperationFact>(2),
                Local<HumanPrincipalPersonalActorFact>(3), delivery is null ? null : JsonSerializer.Deserialize<bool>(delivery));
        }
        var principal = new HumanPrincipalSourceFacts(subject.PrincipalId, tenant,
            Rows<HumanPrincipalAccountFact>(0), Rows<HumanPrincipalBindingFact>(1),
            Rows<HumanPrincipalPlatformGrantFact>(2), Rows<HumanPrincipalRoleFact>(3),
            Rows<HumanPrincipalTenantMembershipFact>(4), Rows<HumanPrincipalTenantGrantFact>(5),
            Rows<HumanPrincipalTenantFact>(6), Rows<HumanPrincipalOwnerMembershipFact>(7),
            Rows<HumanPrincipalOwnerMembershipFact>(8), Rows<HumanPrincipalPermissionFact>(9),
            p.GetProperty("PermissionMappingsExist").GetBoolean(),
            Rows<GuidValue>(11).Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>(12).Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>(13).Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>(14).Select(row => row.Value).ToImmutableArray(), credential);
        return principal;
    }

    internal static HumanEmailSourceFacts Read(string json, HumanCommitSubject subject, Guid? tenant,
        HumanSupportForwardingObservation forwarding, bool local, bool includeRoute)
    {
        using var document = JsonDocument.Parse(json);
        var principal = ReadPrincipal(document.RootElement.GetProperty("Principal"), subject, tenant, local);
        var h = document.RootElement.GetProperty("Support");
        var support = new HumanSupportAccessSourceFacts(forwarding,
            h.GetProperty("q0").Deserialize<ImmutableArray<HumanSupportSessionFact>>(Json),
            h.GetProperty("q1").Deserialize<ImmutableArray<HumanSupportSettingFact>>(Json));
        var routeJson = document.RootElement.GetProperty("Route");
        HumanAuthorizationRouteFacts? route = null;
        if (includeRoute)
        {
            var system = routeJson.GetProperty("System").Deserialize<ImmutableArray<NativeSystem>>(Json);
            var scoped = routeJson.GetProperty("Tenant").Deserialize<ImmutableArray<NativeTenant>>(Json);
            if (system.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key))
                || scoped.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key))
                || !tenant.HasValue && !scoped.IsEmpty)
                throw new InvalidOperationException("human_email_route_projection_scope_invalid");
            var systemByKey = system.ToDictionary(row => row.Key, StringComparer.Ordinal);
            var tenantByKey = scoped.ToDictionary(row => row.Key, StringComparer.Ordinal);
            route = new(subject.PrincipalId, tenant, HumanAuthorizationRouteFacts.Keys.Select(key =>
            {
                systemByKey.TryGetValue(key, out var instance);
                tenantByKey.TryGetValue(key, out var selected);
                return new HumanAuthorizationRouteSettingFact(key,
                    instance is null ? null : new SystemSettingFacts(instance.Value, (SettingValueType)instance.ValueType,
                        instance.IsLocked, instance.Description, instance.Category, instance.AllowedValues),
                    selected is null ? null : new TenantSettingFacts(selected.Value, selected.IsLocked));
            }).ToImmutableArray());
        }
        else if (routeJson.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("human_email_unselected_route_projection");
        return new(principal, support, route);
    }

    private sealed record StringValue(string? Value);
    private sealed record GuidValue(Guid Value);
    private sealed record NativeSystem(string Key, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, string? AllowedValues);
    private sealed record NativeTenant(string Key, string Value, bool IsLocked);
}
