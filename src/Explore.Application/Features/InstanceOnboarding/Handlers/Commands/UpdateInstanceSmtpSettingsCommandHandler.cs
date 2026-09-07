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

        if (!request.Patch.HasChanges() || request.Patch.Configuration.Value is null)
        {
            const string message = "SMTP settings patch must include a complete configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var patch = request.Patch.Configuration.Value;
        var settings = await _smtpSettingService.ReadSettingsAsync();
        settings.Host = patch.Host;
        settings.Port = patch.Port;
        settings.Security = patch.Security;
        settings.FromAddress = patch.FromAddress;
        settings.FromName = patch.FromName;
        settings.TimeoutSeconds = patch.TimeoutSeconds;
        settings.SkipCertificateValidation = patch.SkipCertificateValidation;

        await _smtpSettingService.ApplySettingsAsync(settings);

        _smtpConfigResolver.InvalidateCache();

        return BaseCommandResponse.Success(Guid.Empty, "SMTP settings updated successfully.");
    }
}
