using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record VerifyCerbosEndpointCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required string GrpcEndpoint { get; init; }
}
