// ABOUTME: Generic query handler that resolves all settings for a category through hierarchical cascade.
// ABOUTME: Computes per-setting CanEdit/Reason metadata based on lock state, scope range, and authorization.

namespace Explore.Application.Features.Settings.Handlers.Queries;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Lookups;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

public class ResolveSettingGroupQueryHandler
    : IRequestHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly IPlatformUserRoleRepository _platformRoles;
    private readonly ITenantUserRoleGrantRepository _tenantRoles;
    private readonly ILogger<ResolveSettingGroupQueryHandler> _logger;

    public ResolveSettingGroupQueryHandler(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        ILogger<ResolveSettingGroupQueryHandler> logger,
        IPlatformUserRoleRepository platformRoles,
        ITenantUserRoleGrantRepository tenantRoles)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _platformRoles = platformRoles;
        _tenantRoles = tenantRoles;
        _logger = logger;
    }

    public async Task<SettingGroupResponseDto> Handle(
        ResolveSettingGroupQuery request, CancellationToken cancellationToken)
    {
        var definitions = SettingRegistry.GetByCategory(request.Category);
        if (definitions is null || definitions.Count == 0)
        {
            _logger.LogWarning("Setting category '{Category}' not found in registry", request.Category);
            return new SettingGroupResponseDto
            {
                TenantId = request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
                Category = request.Category,
                Settings = []
            };
        }

        if (request.IncludedKeys is not null)
        {
            definitions = definitions
                .Where(definition => request.IncludedKeys.Contains(definition.Key))
                .ToList();
        }

        Guid? resolvedUserId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
            _adminContext, _currentUserService, cancellationToken);

        var context = SettingCommandHelper.BuildSettingContext(
            request.Scope, _tenantContext, resolvedUserId);

        var keys = definitions.Select(d => d.Key);
        var resolved = await _resolver.ResolveBatchAsync(keys, context, cancellationToken);

        // Email affordances must use the same persisted grants as preview/confirmation, not cached admin claims.
        var isAuthorized = request.Category == "Email" && request.Scope == SettingScope.Tenant
            ? resolvedUserId is { } actorId && actorId != Guid.Empty
                && (await _platformRoles.IsUserPlatformAdmin(actorId)
                    || await _tenantRoles.IsTenantAdminInCurrentTenantAsync(_tenantContext.TenantId, actorId, cancellationToken))
            : await CheckScopeAuthorizationAsync(request.Scope, cancellationToken);
        var tenantCanOmitVerification = request.Scope == SettingScope.Tenant
            ? await _resolver.ResolveWithMetadataAsync(
                GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
                new SettingContext(),
                cancellationToken)
            : null;

        var effectiveSettings = new List<EffectiveSettingDto>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var setting = resolved[i];

            var (canEdit, reason) = ComputeEditability(
                setting, definition, request.Scope, isAuthorized, tenantCanOmitVerification);

            effectiveSettings.Add(new EffectiveSettingDto
            {
                Key = definition.Key,
                Value = setting.Value ?? definition.DefaultValue,
                SettingValueTypeId = (int)setting.ValueType,
                SettingValueTypeCode = NormalizedLookupMetadata.SettingValueType((int)setting.ValueType).Code,
                SettingValueTypeName = NormalizedLookupMetadata.SettingValueType((int)setting.ValueType).Name,
                Source = setting.Source,
                IsLocked = setting.IsLocked,
                IsLockable = definition.IsLockable,
                CanEdit = canEdit,
                Reason = reason,
                Description = setting.Description ?? definition.Description,
                AllowedValues = definition.AllowedValues is { Count: > 0 }
                    ? string.Join(",", definition.AllowedValues)
                    : null
            });
        }

        return new SettingGroupResponseDto
        {
            TenantId = request.Scope == SettingScope.Tenant ? _tenantContext.TenantId : null,
            Category = request.Category,
            Settings = effectiveSettings
        };
    }

    private static (bool CanEdit, string? Reason) ComputeEditability(
        ResolvedSetting resolved, SettingDefinition definition,
        SettingScope requestedScope, bool isAuthorized,
        ResolvedSetting? tenantCanOmitVerification)
    {
        // Check if locked from a scope above the requested one
        if (resolved.IsLocked)
        {
            var (isBlocked, lockReason) = SettingCommandHelper.CheckLockState(resolved, requestedScope);
            if (isBlocked)
                return (false, lockReason);
        }

        // Check scope range
        if (requestedScope < definition.MinScope || requestedScope > definition.MaxScope)
            return (false, $"Not configurable at {requestedScope} scope");

        if (requestedScope == SettingScope.Tenant
            && definition.Key == GovernanceSettingKeys.Organizations.VerificationRequired
            && (!bool.TryParse(resolved.Value ?? definition.DefaultValue, out var requiresVerification)
                || requiresVerification)
            && (tenantCanOmitVerification is null
                || !bool.TryParse(tenantCanOmitVerification.Value, out var canOmit)
                || !canOmit))
        {
            return (false, SettingCommandHelper.TenantVerificationAuthorityError);
        }

        // Check authorization
        if (!isAuthorized)
            return (false, "Insufficient permissions");

        return (true, null);
    }

    private async Task<bool> CheckScopeAuthorizationAsync(
        SettingScope scope, CancellationToken ct)
    {
        if (scope == SettingScope.User)
        {
            return _currentUserService.IsAuthenticated
                && await SettingCommandHelper.ResolveCurrentUserIdAsync(_adminContext, _currentUserService, ct) is not null;
        }

        if (scope == SettingScope.Tenant)
        {
            if (await _adminContext.IsTenantAdminAsync(_tenantContext.TenantId, ct))
            {
                return true;
            }

            Guid? userId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
                _adminContext, _currentUserService, ct);
            if (userId is null)
            {
                return false;
            }

            IReadOnlyList<Guid> adminTenantIds = await _adminContext.GetAdminTenantIdsAsync(userId.Value, ct);
            return adminTenantIds.Contains(_tenantContext.TenantId);
        }

        if (scope == SettingScope.Instance)
        {
            if (await _adminContext.IsInstanceAdminAsync(ct))
            {
                return true;
            }

            Guid? userId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
                _adminContext, _currentUserService, ct);
            return userId is not null && await _adminContext.IsInstanceAdminAsync(userId.Value, ct);
        }

        return scope switch
        {
            _ => false
        };
    }
}
