using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateResolverConfigurationCommandHandler : ICommandHandler<UpdateResolverConfigurationCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IResolverConfigService _resolverConfigService;

    public UpdateResolverConfigurationCommandHandler(
        IAdminContext adminContext,
        IResolverConfigService resolverConfigService)
    {
        _adminContext = adminContext;
        _resolverConfigService = resolverConfigService;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateResolverConfigurationCommand request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        if (!isInstanceAdmin)
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update tenant resolver configuration.");
        }

        if (!request.Patch.HasChanges())
        {
            const string message = "Resolver configuration patch must include at least one setting.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        return await _resolverConfigService.ApplyConfigurationAsync(request, cancellationToken);
    }
}
