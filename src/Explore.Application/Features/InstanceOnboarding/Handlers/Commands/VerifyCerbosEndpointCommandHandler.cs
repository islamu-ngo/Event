using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Utilities;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class VerifyCerbosEndpointCommandHandler : IRequestHandler<VerifyCerbosEndpointCommand, BaseCommandResponse<Guid>>
{
    private readonly IAuthorizationProviderConfigurationService _configurationService;

    public VerifyCerbosEndpointCommandHandler(IAuthorizationProviderConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(VerifyCerbosEndpointCommand request, CancellationToken cancellationToken)
    {
        var normalizedEndpoint = GrpcEndpointNormalizer.Normalize(request.GrpcEndpoint);

        if (!GrpcEndpointNormalizer.IsValid(normalizedEndpoint))
        {
            const string message = "Cerbos gRPC endpoint must be a valid URL or host:port value.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var isReachable = await _configurationService.VerifyCerbosEndpointAsync(normalizedEndpoint, cancellationToken);
        if (!isReachable)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Ensure the endpoint is reachable and serving the gRPC health service."],
                "Cerbos gRPC endpoint could not be verified.");
        }

        return BaseCommandResponse.Success(Guid.Empty, "Cerbos gRPC endpoint verified successfully.");
    }
}
