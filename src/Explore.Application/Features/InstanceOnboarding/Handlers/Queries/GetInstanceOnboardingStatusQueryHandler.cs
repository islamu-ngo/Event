using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetInstanceOnboardingStatusQueryHandler : IRequestHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto>
{
    private readonly IInstanceBootstrapStateRepository _instanceBootstrapStateRepository;
    private readonly IAdminContext _adminContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISetupSecretProvider _setupSecretProvider;
    private readonly IDeploymentModeProvider _deploymentModeProvider;

    public GetInstanceOnboardingStatusQueryHandler(
        IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
        IAdminContext adminContext,
        ICurrentUserService currentUserService,
        ISetupSecretProvider setupSecretProvider,
        IDeploymentModeProvider deploymentModeProvider)
    {
        _instanceBootstrapStateRepository = instanceBootstrapStateRepository;
        _adminContext = adminContext;
        _currentUserService = currentUserService;
        _setupSecretProvider = setupSecretProvider;
        _deploymentModeProvider = deploymentModeProvider;
    }

    public async Task<InstanceOnboardingStatusDto> Handle(GetInstanceOnboardingStatusQuery request, CancellationToken cancellationToken)
    {
        var bootstrap = await _instanceBootstrapStateRepository.GetCurrent(cancellationToken);
        string state = bootstrap switch
        {
            null => "InteractivePending",
            { Status: InstanceBootstrapStatus.Pending, Mode: InstanceBootstrapMode.Interactive } =>
                "InteractivePending",
            { Status: InstanceBootstrapStatus.Pending, Mode: InstanceBootstrapMode.ConfiguredAdministrator } =>
                "ConfiguredAdministratorPending",
            { Status: InstanceBootstrapStatus.Completed } => "Completed",
            _ => "Invalid"
        };
        var isCompleted = state == "Completed";
        var selectedDeploymentMode = bootstrap?.DeploymentMode.ToString()
            ?? (await _deploymentModeProvider.GetConfiguredOnboardingModeAsync(cancellationToken)).ToString();

        var response = new InstanceOnboardingStatusDto
        {
            IsCompleted = isCompleted,
            State = state,
            Mode = bootstrap?.Mode.ToString() ?? InstanceBootstrapMode.Interactive.ToString(),
            Provider = state == "ConfiguredAdministratorPending"
                ? bootstrap!.ProviderKind?.ToString()
                : null,
            Generation = bootstrap?.Generation ?? 1,
            IsAuthenticated = _currentUserService.IsAuthenticated,
            IsCurrentUserInstanceAdmin = false,
            SelectedDeploymentMode = selectedDeploymentMode,
            IsSetupModeActive = _setupSecretProvider.IsSetupModeActive,
            SetupSecretFromEnvironment = _setupSecretProvider.IsFromEnvironmentVariable
        };
        ApplySetupSecretStatus(response, !string.IsNullOrWhiteSpace(_setupSecretProvider.GeneratedSecretFilePath));

        if (!_currentUserService.IsAuthenticated)
        {
            return response;
        }

        response.IsCurrentUserInstanceAdmin = await _adminContext.IsInstanceAdminAsync(cancellationToken);
        return response;
    }

    private static void ApplySetupSecretStatus(InstanceOnboardingStatusDto response, bool generatedSecretCanBeRetrieved)
    {
        if (response.IsCompleted)
        {
            response.SetupSecretState = "Locked";
            response.SetupSecretGuidance = "Setup is complete. The setup secret is locked and can no longer be used.";
            return;
        }

        if (!response.IsSetupModeActive)
        {
            response.SetupSecretState = "Unavailable";
            response.SetupSecretGuidance = "Setup mode is not active. If onboarding is incomplete, configure SETUP_SECRET and restart the application.";
            return;
        }

        if (response.SetupSecretFromEnvironment)
        {
            response.SetupSecretState = "Environment";
            response.SetupSecretGuidance = "Use the SETUP_SECRET environment variable configured for this deployment.";
            return;
        }

        response.SetupSecretState = "Generated";
        response.SetupSecretGuidance = generatedSecretCanBeRetrieved
            ? "Retrieve the generated secret using the Docker-host instruction in the application logs. The secret value itself is never logged."
            : "Setup secrets are never shown in startup output. Configure SETUP_SECRET and restart the application to continue.";
    }

}
