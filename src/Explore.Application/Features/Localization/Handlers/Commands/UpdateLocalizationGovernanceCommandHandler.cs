using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Localization;
using Explore.Application.DTOs.Localization.Validators;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Localization.Handlers.Commands;

public class UpdateLocalizationGovernanceCommandHandler
    : ICommandHandler<UpdateLocalizationGovernanceCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IHumanSettingsMutationPreparation _preparation;
    private readonly IHierarchicalSettingsReader _settings;
    private readonly IEnumerable<INotificationHandler<SettingChangedNotification>> _notificationHandlers;
    private readonly SettingUpsertService _upsertService;
    private readonly ITranslationConfigResolver _configResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UpdateLocalizationGovernanceCommandHandler> _logger;

    public UpdateLocalizationGovernanceCommandHandler(
        IAdminContext adminContext,
        IHumanSettingsMutationPreparation preparation,
        IHierarchicalSettingsReader settings,
        IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers,
        SettingUpsertService upsertService,
        ITranslationConfigResolver configResolver,
        ITenantContext tenantContext,
        ILogger<UpdateLocalizationGovernanceCommandHandler> logger)
    {
        _adminContext = adminContext;
        _preparation = preparation;
        _settings = settings;
        _notificationHandlers = notificationHandlers;
        _upsertService = upsertService;
        _configResolver = configResolver;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateLocalizationGovernanceCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actor.HasValue || !await _adminContext.IsInstanceAdminAsync(actor.Value, cancellationToken))
        {
            const string message = "Instance administrator authority is required to update localization governance.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var validator = new UpdateLocalizationGovernanceDtoValidator();
        var validation = await validator.ValidateAsync(request.Dto, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(e => e.ErrorMessage),
                "Localization governance update failed.");
        }

        var effects = OrdinarySettingsMutation.CaptureLocalization(request);
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        var notifications = await _preparation.ExecutePreparedAsync(prepared, async (_, token) =>
        {
            var committed = new List<SettingChangedNotification>();
            var occurredAt = DateTime.UtcNow;
            foreach (var effect in effects)
            {
                var result = await _upsertService.UpsertInstanceValueInCurrentTransactionAsync(
                    new(effect.Key, effect.Value!, actor, occurredAt), token);
                committed.Add(result.Notification);
            }
            return committed;
        }, cancellationToken);

        _settings.InvalidateCache();
        foreach (var notification in notifications)
            await _notificationHandlers.HandleAsync(notification, CancellationToken.None);
        var dto = request.Dto;
        var enabledLanguagesCsv = dto.Languages is { } languages
            ? string.Join(",", languages.EnabledLanguages.Select(code => code.Trim().ToLowerInvariant()))
            : string.Empty;

        _configResolver.InvalidateCache(_tenantContext.TenantId);

        _logger.LogInformation(
            "[LOCALIZATION] Governance updated by {Actor}: provider={Provider}, enabled=[{Enabled}], fallback={Fallback}, pickerEnabled={Picker}, forceOffline={ForceOffline}",
            actor, dto.Tms?.Provider, enabledLanguagesCsv, dto.Languages?.FallbackLanguage, dto.Runtime?.ClientPickerEnabled, dto.Runtime?.ForceOfflineMode);

        return BaseCommandResponse.Success(actor.Value, "Localization governance updated successfully.");
    }
}
