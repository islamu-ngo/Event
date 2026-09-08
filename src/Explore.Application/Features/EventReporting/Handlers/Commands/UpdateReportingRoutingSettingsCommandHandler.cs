using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class UpdateReportingRoutingSettingsCommandHandler(
    ITenantContext tenantContext,
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateReportingRoutingSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateReportingRoutingSettingsCommand request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId == Guid.Empty ? request.TenantId : tenantContext.TenantId;

        if (!await IsUserAuthorizedAsync(tenantId, request.UserId, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only tenant administrators or instance administrators can update moderation reporting routing settings.");
        }

        TenantDelegationSettingGroup delegation = await settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
            new SettingContext(tenantId),
            cancellationToken);

        if (delegation.LockReportingProviders)
        {
            return Locked("Tenant moderation reporting provider settings are locked by instance policy.");
        }

        if (request.Settings.Osprey is not null && delegation.LockTenantOspreyProvider)
        {
            return Locked("Tenant Osprey reporting provider settings are locked by instance policy.");
        }

        if (request.Settings.Coop is not null && delegation.LockTenantCoopProvider)
        {
            return Locked("Tenant Coop reporting provider settings are locked by instance policy.");
        }

        List<string> validationErrors = Validate(request.Settings);
        if (validationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationErrors,
                "Moderation reporting routing settings validation failed.");
        }

        await unitOfWork.ExecuteInTransactionAsync(
            ct => ApplySettingsAsync(tenantId, request.UserId, request.Settings, ct),
            cancellationToken);

        settingsResolver.InvalidateCache(SettingScope.Tenant, tenantId);

        return BaseCommandResponse.Success(
            tenantId,
            "Moderation reporting routing settings updated successfully.");
    }

    private async Task<bool> IsUserAuthorizedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        IReadOnlyList<Guid> adminTenantIds = await adminContext.GetAdminTenantIdsAsync(userId, cancellationToken);
        if (adminTenantIds.Contains(tenantId))
        {
            return true;
        }

        return await adminContext.IsInstanceAdminAsync(userId, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Locked(string message) =>
        BaseCommandResponse.Failure<Guid>(
            FailureCodes.ReportingTenantOverridesLocked,
            message,
            ["Instance reporting delegation must be unlocked before tenant reporting provider overrides can be saved."]);

    private static List<string> Validate(UpdateReportingRoutingSettingsDto settings)
    {
        var errors = new List<string>();

        if (settings.Policy is null && settings.Osprey is null && settings.Coop is null)
        {
            errors.Add("At least one moderation reporting routing group is required.");
        }

        if (settings.Osprey is { } osprey && !IsRoutingMode(osprey.RoutingMode))
        {
            errors.Add("Osprey routing mode must be instance, tenant, or both.");
        }

        if (settings.Coop is { } coop && !IsRoutingMode(coop.RoutingMode))
        {
            errors.Add("Coop routing mode must be instance, tenant, or both.");
        }

        if (settings.Policy is { } policy && !Enum.IsDefined(policy.EvidenceMode))
        {
            errors.Add("Evidence mode is invalid.");
        }

        AddEndpointError(errors, settings.Osprey?.EndpointUrl, "Osprey endpoint URL");
        AddEndpointError(errors, settings.Coop?.EndpointUrl, "Coop endpoint URL");

        return errors;
    }

    private static bool IsRoutingMode(string? value) =>
        string.Equals(value, ReportingRoutingMode.Instance, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, ReportingRoutingMode.Tenant, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, ReportingRoutingMode.Both, StringComparison.OrdinalIgnoreCase);

    private static void AddEndpointError(List<string> errors, string? endpointUrl, string label)
    {
        if (endpointUrl is null || endpointUrl.Length == 0)
        {
            return;
        }

        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add($"{label} must be an absolute HTTP or HTTPS URL.");
        }
    }

    private async Task ApplySettingsAsync(
        Guid tenantId,
        Guid userId,
        UpdateReportingRoutingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (settings.Policy is { } policy)
        {
            await SetAsync(GovernanceSettingKeys.Reporting.TenantExternalSyncEnabled, policy.ExternalSyncEnabled, tenantId, userId, cancellationToken);
            await SetAsync(GovernanceSettingKeys.Reporting.EvidenceMode, policy.EvidenceMode.ToString(), tenantId, userId, cancellationToken);
        }

        if (settings.Osprey is { } osprey)
        {
            await ApplyProviderAsync(
                GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
                GovernanceSettingKeys.Reporting.OspreyRoutingMode,
                GovernanceSettingKeys.Reporting.OspreyEndpointUrl,
                InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
                InfrastructureSecretSettingKeys.Reporting.OspreyWebhookSecret,
                osprey,
                tenantId,
                userId,
                cancellationToken);
        }

        if (settings.Coop is { } coop)
        {
            await ApplyProviderAsync(
                GovernanceSettingKeys.Reporting.EnableTenantCoopProvider,
                GovernanceSettingKeys.Reporting.CoopRoutingMode,
                GovernanceSettingKeys.Reporting.CoopEndpointUrl,
                InfrastructureSecretSettingKeys.Reporting.CoopApiKey,
                InfrastructureSecretSettingKeys.Reporting.CoopWebhookSecret,
                coop,
                tenantId,
                userId,
                cancellationToken);
        }
    }

    private async Task ApplyProviderAsync(
        string enabledKey,
        string routingModeKey,
        string endpointKey,
        string apiKey,
        string webhookSecretKey,
        ReportingProviderRoutingUpdateDto provider,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await SetAsync(enabledKey, provider.Enabled, tenantId, userId, cancellationToken);
        await SetAsync(routingModeKey, NormalizeRoutingMode(provider.RoutingMode), tenantId, userId, cancellationToken);
        await SetIfProvidedAsync(endpointKey, provider.EndpointUrl, tenantId, userId, cancellationToken, writeWhitespace: true);

        if (provider.Credentials is { } credentials)
        {
            await SetIfProvidedAsync(apiKey, credentials.ApiKey, tenantId, userId, cancellationToken);
            await SetIfProvidedAsync(webhookSecretKey, credentials.WebhookSecret, tenantId, userId, cancellationToken);
        }
    }

    private Task SetAsync<T>(string key, T value, Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        settingsResolver.SetValueAsync(
            key,
            SettingValueSerializer.Serialize(value),
            SettingScope.Tenant,
            tenantId,
            userId,
            cancellationToken);

    private async Task SetIfProvidedAsync(
        string key,
        string? value,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken,
        bool writeWhitespace = false)
    {
        if (value is null || (!writeWhitespace && string.IsNullOrWhiteSpace(value)))
        {
            return;
        }

        await SetAsync(key, value, tenantId, userId, cancellationToken);
    }

    private static string NormalizeRoutingMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        ReportingRoutingMode.Instance => ReportingRoutingMode.Instance,
        ReportingRoutingMode.Tenant => ReportingRoutingMode.Tenant,
        _ => ReportingRoutingMode.Both
    };
}
