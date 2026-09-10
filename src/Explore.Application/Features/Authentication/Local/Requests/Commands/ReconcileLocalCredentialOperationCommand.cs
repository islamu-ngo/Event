
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record ReconcileLocalCredentialOperationCommand : IRequest<BaseCommandResponse<Guid>>
{
    public ReconcileLocalCredentialOperationCommand(Guid operationId)
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}
