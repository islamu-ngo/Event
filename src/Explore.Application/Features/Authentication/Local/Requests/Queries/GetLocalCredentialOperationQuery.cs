
using Explore.Application.Contracts.Identity;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Queries;

public sealed record GetLocalCredentialOperationQuery : IRequest<LocalCredentialOperationStatus?>
{
    public GetLocalCredentialOperationQuery(Guid operationId)
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}
