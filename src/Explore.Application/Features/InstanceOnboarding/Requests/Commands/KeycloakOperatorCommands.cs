using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record PlanKeycloakOperationCommand(KeycloakOperationPlanInputDto Input) : ICommand<KeycloakOperationDto>;

public sealed record ApplyKeycloakOperationCommand(Guid OperationId, KeycloakOperationCredentialsDto Input) : ICommand<KeycloakOperationDto>;

public sealed record ReconcileKeycloakOperationCommand(Guid OperationId, KeycloakOperationCredentialsDto Input) : ICommand<KeycloakOperationDto>;

public sealed record CancelKeycloakOperationCommand(Guid OperationId) : ICommand<KeycloakOperationDto>;
