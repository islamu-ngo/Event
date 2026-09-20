using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record VerifyCerbosEndpointCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required string GrpcEndpoint { get; init; }
}
