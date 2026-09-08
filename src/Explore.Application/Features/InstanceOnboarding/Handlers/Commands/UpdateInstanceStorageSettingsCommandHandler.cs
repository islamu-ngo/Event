using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateInstanceStorageSettingsCommandHandler : IRequestHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceStorageSettingService _storageSettingService;
    private readonly IS3ConfigResolver _s3ConfigResolver;

    public UpdateInstanceStorageSettingsCommandHandler(
        IAdminContext adminContext,
        IInstanceStorageSettingService storageSettingService,
        IS3ConfigResolver s3ConfigResolver)
    {
        _adminContext = adminContext;
        _storageSettingService = storageSettingService;
        _s3ConfigResolver = s3ConfigResolver;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateInstanceStorageSettingsCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update storage settings.");
        }

        if (!request.Patch.HasChanges()
            || request.Patch.Policy is { HasValue: true, Value: null }
            || request.Patch.S3Configuration is { HasValue: true, Value: null })
        {
            const string message = "Storage settings patch must include a complete policy or S3 configuration group.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var settings = await _storageSettingService.ReadSettingsAsync(cancellationToken);
        if (request.Patch.Policy.HasValue)
        {
            var policy = request.Patch.Policy.Value!;
            settings.Provider = policy.Provider;
            settings.DefaultMaxUploadBytes = policy.DefaultMaxUploadBytes;
            settings.DefaultTenantQuotaBytes = policy.DefaultTenantQuotaBytes;
            settings.InstanceMaxUploadBytes = policy.InstanceMaxUploadBytes;
            settings.LockTenantStorage = policy.LockTenantStorage;
            settings = settings with { Routes = policy.Routes };
        }

        if (request.Patch.S3Configuration.HasValue)
        {
            var s3 = request.Patch.S3Configuration.Value!;
            settings.S3Endpoint = s3.Endpoint;
            settings.S3PublicEndpoint = s3.PublicEndpoint;
            settings.S3BucketName = s3.BucketName;
            settings.S3Region = s3.Region;
            settings.S3ForcePathStyle = s3.ForcePathStyle;
            settings.S3UploadUrlExpirationMinutes = s3.UploadUrlExpirationMinutes;
        }

        var validator = new InstanceStorageSettingsDtoValidator();
        var validationResult = await validator.ValidateAsync(settings, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Storage settings validation failed.");
        }

        await _storageSettingService.ApplySettingsAsync(settings, request.Patch);

        // Invalidate S3 config cache so optional S3 provider changes take effect immediately.
        _s3ConfigResolver.InvalidateCache();

        return BaseCommandResponse.Success(Guid.Empty, "Storage settings updated successfully.");
    }
}
