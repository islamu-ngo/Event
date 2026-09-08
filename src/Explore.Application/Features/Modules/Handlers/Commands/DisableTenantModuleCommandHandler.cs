using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Modules.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Modules.Handlers.Commands;

public sealed class DisableTenantModuleCommandHandler(IModuleService moduleService)
    : IRequestHandler<DisableTenantModuleCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        DisableTenantModuleCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ModuleKey))
        {
            return Failure(request.TenantId, "Module key is required.");
        }

        var success = await moduleService.DisableModuleAsync(
            request.TenantId,
            request.ModuleKey,
            cancellationToken);

        return success
            ? BaseCommandResponse.Success(request.TenantId, "Module disabled.")
            : Failure(request.TenantId, $"Module '{request.ModuleKey}' is not enabled for this tenant.");
    }

    private static BaseCommandResponse<Guid> Failure(Guid tenantId, string message) =>
        BaseCommandResponse.Validation<Guid>([message], message, tenantId);
}
