
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record ReconcileLocalCredentialOperationCommand : ICommand<BaseCommandResponse<Guid>>
{
    public ReconcileLocalCredentialOperationCommand(Guid operationId)
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}
