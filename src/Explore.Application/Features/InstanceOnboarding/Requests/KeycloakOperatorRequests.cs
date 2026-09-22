using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests;

public sealed record GetKeycloakConnectionQuery : IQuery<KeycloakConnectionDto>;

public sealed record InspectKeycloakOperationQuery(KeycloakOperationInput Input) : IQuery<KeycloakInspectionDto>;

public sealed record PlanKeycloakOperationCommand(KeycloakOperationInput Input) : ICommand<KeycloakOperationDto>;

public sealed record GetKeycloakOperationQuery(Guid OperationId) : IQuery<KeycloakOperationDto?>;

public sealed record ApplyKeycloakOperationCommand(Guid OperationId, KeycloakOperationInput Input) : ICommand<KeycloakOperationDto>;

public sealed record ReconcileKeycloakOperationCommand(Guid OperationId, KeycloakOperationInput Input) : ICommand<KeycloakOperationDto>;

public sealed record CancelKeycloakOperationCommand(Guid OperationId) : ICommand<KeycloakOperationDto>;
