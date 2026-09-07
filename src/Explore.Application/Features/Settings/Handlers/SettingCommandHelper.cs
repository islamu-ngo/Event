namespace Explore.Application.Features.Settings.Handlers;

using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

internal static class SettingCommandHelper
{
    /// <summary>
    /// Validates a plain-text value against its definition's ValueType and AllowedValues,
    /// then serializes to JSON storage format.
    /// </summary>
    internal static (bool IsValid, string? SerializedValue, string? Error) ValidateAndSerialize(
        string plainValue, SettingDefinition definition)
    {
        plainValue = NormalizePlainValue(plainValue, definition);

        switch (definition.ValueType)
        {
            case SettingValueType.String:
                if (definition.AllowedValues is { Count: > 0 } &&
                    !definition.AllowedValues.Contains(plainValue, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, null,
                        $"Value '{plainValue}' is not allowed. Allowed values: {string.Join(", ", definition.AllowedValues)}");
                }
                return (true, SettingValueSerializer.Serialize(plainValue), null);

            case SettingValueType.Integer:
                if (!int.TryParse(plainValue, out var intValue))
                    return (false, null, $"Value '{plainValue}' is not a valid integer.");
                return (true, SettingValueSerializer.Serialize(intValue), null);

            case SettingValueType.Long:
                if (!long.TryParse(plainValue, out var longValue))
                    return (false, null, $"Value '{plainValue}' is not a valid long integer.");
                return (true, SettingValueSerializer.Serialize(longValue), null);

            case SettingValueType.Boolean:
                if (!bool.TryParse(plainValue, out var boolValue))
                    return (false, null, $"Value '{plainValue}' is not a valid boolean.");
                return (true, SettingValueSerializer.Serialize(boolValue), null);

            case SettingValueType.Decimal:
                if (!decimal.TryParse(plainValue, System.Globalization.CultureInfo.InvariantCulture,
                    out var decValue))
                    return (false, null, $"Value '{plainValue}' is not a valid decimal.");
                return (true, SettingValueSerializer.Serialize(decValue), null);

            case SettingValueType.Json:
                try
                {
                    using JsonDocument _ = JsonDocument.Parse(plainValue);
                    return (true, plainValue, null);
                }
                catch (JsonException)
                {
                    return (false, null, "Value is not valid JSON.");
                }

            case SettingValueType.DateTime:
                if (!DateTime.TryParse(plainValue, System.Globalization.CultureInfo.InvariantCulture,
                    out var dtValue))
                    return (false, null, $"Value '{plainValue}' is not a valid datetime.");
                return (true, SettingValueSerializer.Serialize(dtValue), null);

            default:
                return (true, plainValue, null);
        }
    }

    private static string NormalizePlainValue(string plainValue, SettingDefinition definition)
    {
        if (definition.ValueType != SettingValueType.String)
            return plainValue;

        return definition.Key switch
        {
            GovernanceSettingKeys.Cerbos.GrpcEndpoint
                or GovernanceSettingKeys.Cerbos.CustomEndpoint
                or GovernanceSettingKeys.Cerbos.CustomAdminEndpoint => GrpcEndpointNormalizer.Normalize(plainValue),
            _ => plainValue
        };
    }

    /// <summary>
    /// Maps a SettingScope to the corresponding SettingSource for notification publishing.
    /// </summary>
    internal static SettingSource MapScopeToSource(SettingScope scope)
    {
        return scope switch
        {
            SettingScope.Instance => SettingSource.SystemDefault,
            SettingScope.Tenant => SettingSource.TenantOverride,
            SettingScope.Organization => SettingSource.OrganizationOverride,
            SettingScope.Group => SettingSource.GroupOverride,
            SettingScope.User => SettingSource.UserPreference,
            _ => SettingSource.SystemDefault
        };
    }

    /// <summary>
    /// Builds a SettingContext appropriate for the requested scope depth.
    /// </summary>
    internal static SettingContext BuildSettingContext(
        SettingScope scope, ITenantContext tenantContext, ICurrentUserService currentUserService)
    {
        return BuildSettingContext(scope, tenantContext, currentUserService.UserId);
    }

    /// <summary>
    /// Builds a SettingContext with an already-resolved internal user ID.
    /// </summary>
    internal static SettingContext BuildSettingContext(
        SettingScope scope, ITenantContext tenantContext, Guid? userId)
    {
        return scope switch
        {
            SettingScope.Instance => new SettingContext(null, null, null, null),
            SettingScope.Tenant => new SettingContext(tenantContext.TenantId, null, null, null),
            SettingScope.User => new SettingContext(tenantContext.TenantId, null, null, userId),
            _ => new SettingContext(tenantContext.TenantId, null, null, null)
        };
    }

    /// <summary>
    /// Determines scope entity ID and actor ID for the current operation.
    /// </summary>
    internal static (Guid ScopeId, Guid ActorId) GetScopeAndActorIds(
        SettingScope scope, ITenantContext tenantContext, ICurrentUserService currentUserService)
    {
        return GetScopeAndActorIds(scope, tenantContext, currentUserService.UserId);
    }

    /// <summary>
    /// Determines scope entity ID and actor ID using an already-resolved internal user ID.
    /// </summary>
    internal static (Guid ScopeId, Guid ActorId) GetScopeAndActorIds(
        SettingScope scope, ITenantContext tenantContext, Guid? resolvedUserId)
    {
        var userId = resolvedUserId ?? Guid.Empty;
        return scope switch
        {
            SettingScope.User => (userId, userId),
            SettingScope.Tenant => (tenantContext.TenantId, userId),
            SettingScope.Instance => (Guid.Empty, userId),
            _ => (Guid.Empty, userId)
        };
    }

    internal static async Task<Guid?> ResolveCurrentUserIdAsync(
        IAdminContext adminContext,
        ICurrentUserService currentUserService,
        CancellationToken ct)
    {
        return currentUserService.UserId ?? await adminContext.ResolveUserIdAsync(ct);
    }

    internal const string TenantVerificationAuthorityError =
        "Tenant administrators cannot disable organization verification because the instance does not allow omission.";

    internal static async Task<string?> ValidateTenantVerificationOverrideAsync(
        string key,
        string plainValue,
        SettingScope scope,
        IHierarchicalSettingsResolver resolver,
        CancellationToken ct)
    {
        if (scope != SettingScope.Tenant
            || key != GovernanceSettingKeys.Organizations.VerificationRequired
            || !bool.TryParse(plainValue, out var requiresVerification)
            || requiresVerification)
        {
            return null;
        }

        var authority = await resolver.ResolveWithMetadataAsync(
            GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
            new SettingContext(),
            ct);

        return authority is not null
            && bool.TryParse(authority.Value, out var canOmit)
            && canOmit
            ? null
            : TenantVerificationAuthorityError;
    }

    /// <summary>
    /// Checks if the current user has authorization to write settings at the given scope.
    /// </summary>
    internal static async Task<(bool Authorized, string? Error)> CheckAuthorizationAsync(
        SettingScope scope,
        IAdminContext adminContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        CancellationToken ct)
    {
        switch (scope)
        {
            case SettingScope.User:
                if (!currentUserService.IsAuthenticated)
                    return (false, "Authentication required to update user preferences.");
                if (await ResolveCurrentUserIdAsync(adminContext, currentUserService, ct) is null)
                    return (false, "Unable to resolve authenticated user.");
                return (true, null);

            case SettingScope.Tenant:
                var isTenantAdmin = await adminContext.IsTenantAdminAsync(tenantContext.TenantId, ct);
                if (isTenantAdmin)
                    return (true, null);

                var tenantUserId = await ResolveCurrentUserIdAsync(adminContext, currentUserService, ct);
                if (tenantUserId is not null)
                {
                    var adminTenantIds = await adminContext.GetAdminTenantIdsAsync(tenantUserId.Value, ct);
                    if (adminTenantIds.Contains(tenantContext.TenantId))
                    {
                        return (true, null);
                    }
                }

                return (false, "Only tenant administrators can update tenant settings.");

            case SettingScope.Instance:
                if (await adminContext.IsInstanceAdminAsync(ct))
                    return (true, null);

                var instanceUserId = await ResolveCurrentUserIdAsync(adminContext, currentUserService, ct);
                if (instanceUserId is not null
                    && await adminContext.IsInstanceAdminAsync(instanceUserId.Value, ct))
                {
                    return (true, null);
                }

                return (false, "Only instance administrators can update instance settings.");

            default:
                return (false, $"Scope '{scope}' is not supported for setting operations.");
        }
    }

    /// <summary>
    /// Checks if a resolved setting is locked from a scope above the requested scope.
    /// Returns true if the setting can be edited (not locked from above).
    /// </summary>
    internal static (bool IsBlockedByLock, string? LockReason) CheckLockState(
        ResolvedSetting resolved, SettingScope requestedScope)
    {
        if (!resolved.IsLocked)
            return (false, null);

        var lockScope = resolved.Source switch
        {
            SettingSource.SystemLocked => SettingScope.Instance,
            SettingSource.TenantLocked => SettingScope.Tenant,
            _ => requestedScope
        };

        if (lockScope < requestedScope)
        {
            var lockSource = lockScope == SettingScope.Instance ? "instance" : "tenant";
            return (true, $"Locked by {lockSource} administrator");
        }

        return (false, null);
    }
}
