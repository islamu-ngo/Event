using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class SyncAuthorizationPolicyPackageCommandHandler(
    IPolicyPackageService policyPackageService,
    IAuthorizationProviderConfigurationService configurationService,
    ILogger<SyncAuthorizationPolicyPackageCommandHandler> logger)
    : IRequestHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SyncAuthorizationPolicyPackageCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new AuthorizationPolicyPackageSyncRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Invalid Cerbos policy synchronization request.");
        }

        PolicyPackageAdminCredentials? oneTimeCredentials =
            string.IsNullOrWhiteSpace(request.Request.AdminUsername)
                ? null
                : new(
                    request.Request.AdminUsername.Trim(),
                    request.Request.AdminPassword!);

        try
        {
            var configuration = await configurationService.ReadConfigurationAsync();
            if (configuration.AuthorizationProviderManagedByDeployment)
            {
                var reconciliation = await configurationService
                    .ReconcileDeploymentProviderAsync(cancellationToken, oneTimeCredentials);
                return reconciliation.Succeeded
                    ? BaseCommandResponse.Success(Guid.Empty, reconciliation.Message)
                    : BaseCommandResponse.Validation<Guid>([reconciliation.Message], reconciliation.Message);
            }

            var result = await policyPackageService.PublishAsync(cancellationToken, oneTimeCredentials);

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Authorization policy package synced. PackageId={PackageId} ContentHash={ContentHash}",
                    result.PackageId,
                    result.ContentHash);

                return BaseCommandResponse.Success(
                    Guid.Empty,
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "Authorization policy package synced successfully."
                        : result.Message);
            }

            logger.LogWarning(
                "Authorization policy package sync failed. PackageId={PackageId} IssueCode={IssueCode} Message={Message}",
                result.PackageId,
                result.IssueCode,
                result.Message);

            string message = string.IsNullOrWhiteSpace(result.Message)
                ? "Authorization policy package sync failed."
                : result.Message;
            return result.IssueCode == PolicyPackageIssueCode.None
                ? BaseCommandResponse.Validation(
                    result.Warnings.Count > 0 ? result.Warnings : [message],
                    message,
                    Guid.Empty)
                : BaseCommandResponse.Failure<Guid>(
                    result.IssueCode.ToString(),
                    message,
                    result.Warnings.Count > 0 ? result.Warnings : null,
                    Guid.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authorization policy package sync failed unexpectedly.");
            return BaseCommandResponse.Validation(
                ["Review server logs for the safe failure details and retry."],
                "Authorization policy package sync failed.",
                Guid.Empty);
        }
    }
}
