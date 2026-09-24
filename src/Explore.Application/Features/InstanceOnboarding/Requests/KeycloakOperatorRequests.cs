using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests;

public sealed record GetKeycloakConnectionQuery : IQuery<KeycloakConnectionDto>;

public sealed record InspectKeycloakOperationQuery(KeycloakInspectionCredentials Input) : IQuery<KeycloakInspectionDto>;

public sealed record PlanKeycloakOperationCommand(KeycloakOperationPlanInput Input) : ICommand<KeycloakOperationDto>;

public sealed record GetKeycloakOperationQuery(Guid OperationId) : IQuery<KeycloakOperationDto?>;

public sealed record ApplyKeycloakOperationCommand(Guid OperationId, KeycloakOperationCredentials Input) : ICommand<KeycloakOperationDto>;

public sealed record ReconcileKeycloakOperationCommand(Guid OperationId, KeycloakOperationCredentials Input) : ICommand<KeycloakOperationDto>;

public sealed record CancelKeycloakOperationCommand(Guid OperationId) : ICommand<KeycloakOperationDto>;
