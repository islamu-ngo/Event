
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class CompleteLocalInstanceOnboardingCommandHandler(
    LocalAdministratorBootstrapOperation operation,
    IAuthenticationProviderDispatcher providers,
    ISetupSecretProvider setup,
    IDeploymentModeProvider deployment,
    ISender sender) : IRequestHandler<CompleteLocalInstanceOnboardingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CompleteLocalInstanceOnboardingCommand command, CancellationToken cancellationToken)
    {
        if (!command.SetupPrincipal.Identities.Any(identity => identity.IsAuthenticated
                && identity.AuthenticationType == ApiAuthenticationSchemeNames.SetupSecret)
            || !await setup.IsSetupModeActiveAsync(cancellationToken))
            return Failure("local_bootstrap_authority_invalid");
        if (await providers.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
            return Failure("local_bootstrap_provider_inactive");

        var request = command.Request;
        if (request.Settings is null) return Failure("local_bootstrap_settings_invalid");
        request = request with { Settings = request.Settings with
        {
            DeploymentMode = await deployment.GetConfiguredOnboardingModeAsync(cancellationToken)
        } };
        var validation = await new CompleteLocalInstanceOnboardingRequestDtoValidator().ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Failure("local_bootstrap_request_invalid");
        var preflight = await sender.Send(new GetOnboardingPreflightQuery(), cancellationToken);
        if (!preflight.IsReadyToLaunch) return Failure("local_bootstrap_preflight_blocked");

        return await operation.CompleteInteractiveAsync(
            operationId: request.OperationId, username: request.Username, temporaryPassword: request.TemporaryPassword,
            email: request.Email, firstName: request.FirstName, lastName: request.LastName,
            settings: request.Settings, setupPrincipal: command.SetupPrincipal, cancellationToken: cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(string code) =>
        BaseCommandResponse.Failure<Guid>(code, "Local administrator bootstrap did not complete.");
}
