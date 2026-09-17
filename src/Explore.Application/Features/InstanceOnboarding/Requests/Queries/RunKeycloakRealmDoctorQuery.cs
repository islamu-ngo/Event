using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record RunKeycloakRealmDoctorQuery : IQuery<KeycloakRealmDoctorResultDto>
{
    public KeycloakRealmDoctorRequestDto Request { get; init; } = new();
}
