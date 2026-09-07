// ABOUTME: Handler for updating the instance SMTP (email delivery) settings.
// ABOUTME: Validates input, persists SMTP config to the infrastructure config store.
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateInstanceSmtpSettingsCommandHandler : IRequestHandler<UpdateInstanceSmtpSettingsCommand, BaseCommandResponse<Guid>>
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

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateInstanceSmtpSettingsCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
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

        await _smtpSettingService.ApplySettingsAsync(settings, actorUserId: request.UserId,
            enableDelivery: request.Patch.DeliveryEnabled.HasValue, cancellationToken: cancellationToken);

        _smtpConfigResolver.InvalidateCache();

        return BaseCommandResponse.Success(Guid.Empty, "SMTP settings updated successfully.");
    }
}
