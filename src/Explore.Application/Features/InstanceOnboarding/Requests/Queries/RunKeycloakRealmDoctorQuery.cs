using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record RunKeycloakRealmDoctorQuery : IRequest<KeycloakRealmDoctorResultDto>
{
    public KeycloakRealmDoctorRequestDto Request { get; init; } = new();
}
