// ABOUTME: Defines explicit administrator reconciliation intent for one durable Local credential operation.
// ABOUTME: Accepts only the operation identifier; actor authority and binding facts are resolved server-side.

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
