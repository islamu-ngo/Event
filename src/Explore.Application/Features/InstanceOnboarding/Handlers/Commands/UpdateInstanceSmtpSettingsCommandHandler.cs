using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateInstanceSmtpSettingsCommandHandler : ICommandHandler<UpdateInstanceSmtpSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceSmtpSettingService _smtpSettingService;
    private readonly ISmtpConfigResolver _smtpConfigResolver;

    public UpdateInstanceSmtpSettingsCommandHandler(
        IAdminContext adminContext,
        IInstanceSmtpSettingService smtpSettingService,
        ISmtpConfigResolver smtpConfigResolver)
    {
        _adminContext = adminContext;
        _smtpSettingService = smtpSettingService;
        _smtpConfigResolver = smtpConfigResolver;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateInstanceSmtpSettingsCommand request, CancellationToken cancellationToken)
    {
        Guid? actor = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (actor is null || actor == Guid.Empty
            || !await _adminContext.IsInstanceAdminAsync(actor.Value, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update SMTP settings.");
        }

        if (!request.Patch.HasChanges()
            || (request.Patch.Configuration.HasValue && request.Patch.Configuration.Value is null))
        {
            const string message = "SMTP settings patch must include delivery enablement or a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }
        if (request.Patch.DeliveryEnabled.HasValue && !request.Patch.DeliveryEnabled.Value)
        {
            const string message = "Disabling email delivery requires a preview and confirmation.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var settings = request.Patch.Configuration.Value is { } patch
            ? new InstanceSmtpSettingsDto
            {
                Host = patch.Host,
                Port = patch.Port,
                Security = patch.Security,
                FromAddress = patch.FromAddress,
                FromName = patch.FromName,
                TimeoutSeconds = patch.TimeoutSeconds,
                SkipCertificateValidation = patch.SkipCertificateValidation
            }
            : null;

        try
        {
            // The selected email writer acquires current instance authority in its original
            // Serializable owner before settings, revisions, or suppression can change.
            await _smtpSettingService.ApplySettingsAsync(settings, actorUserId: actor.Value,
                enableDelivery: request.Patch.DeliveryEnabled.HasValue, cancellationToken: cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update SMTP settings.");
        }

        _smtpConfigResolver.InvalidateCache();

        return BaseCommandResponse.Success(Guid.Empty, "SMTP settings updated successfully.");
    }
}
