using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests;

public sealed record GetKeycloakConnectionQuery : IQuery<KeycloakConnectionDto>;

public sealed record InspectKeycloakOperationQuery(KeycloakInspectionCredentialsDto Input) : IQuery<KeycloakInspectionDto>;

public sealed record GetKeycloakOperationQuery(Guid OperationId) : IQuery<KeycloakOperationDto?>;
