using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class SyncAuthorizationPolicyPackageCommandHandler(
    IPolicyPackageService policyPackageService,
    IAuthorizationProviderConfigurationService configurationService,
    ILogger<SyncAuthorizationPolicyPackageCommandHandler> logger,
    IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
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
                    request.Request.AdminUsername,
                    request.Request.AdminPassword!);

        try
        {
            if (request.Request.InspectOnly)
            {
                var configuration = await configurationService.ReadConfigurationAsync();
                var status = await policyPackageService.GetStatusAsync(cancellationToken, oneTimeCredentials,
                    configuration.AuthorizationProviderManagedByDeployment);
                return status.IsHealthy
                    ? BaseCommandResponse.Success(Guid.Empty, status.Message)
                    : BaseCommandResponse.Failure<Guid>(status.IssueCode.ToString(), status.Message);
            }

            await using var selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
            var result = await selected.Services.GetRequiredService<IPolicyPackageService>().PublishAsync(request, cancellationToken);

            if (result.Succeeded)
            {
                logger.LogInformation("Authorization policy package synced.");

                return BaseCommandResponse.Success(
                    Guid.Empty,
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "Authorization policy package synced successfully."
                        : result.Message);
            }

            logger.LogWarning(
                "Authorization policy package sync failed. IssueCode={IssueCode}",
                result.IssueCode);

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
        catch (Exception)
        {
            logger.LogError("Authorization policy package sync failed.");
            return BaseCommandResponse.Validation(
                ["Review server logs for the safe failure details and retry."],
                "Authorization policy package sync failed.",
                Guid.Empty);
        }
    }
}
