using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterGovernanceSettingsCommandHandler(
    IFooterSettingsWriter writer,
    IFooterOperationExecution footerExecution)
    : ICommandHandler<UpdateFooterGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateFooterGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        await footerExecution.RequireOwnedAsync(request, cancellationToken);

        if (!request.Patch.HasChanges())
            return BaseCommandResponse.Validation<Guid>(
                ["At least one footer governance setting must be provided."],
                "At least one footer governance setting must be provided.");

        await writer.WriteGovernanceAsync(request, cancellationToken);

        return BaseCommandResponse.Success(Guid.Empty, "Footer governance settings updated successfully.");
    }
}
