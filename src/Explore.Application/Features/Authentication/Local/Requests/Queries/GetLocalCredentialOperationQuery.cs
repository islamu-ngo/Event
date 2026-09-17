
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Authentication.Local.Requests.Queries;

public sealed record GetLocalCredentialOperationQuery : IQuery<LocalCredentialOperationStatus?>
{
    public GetLocalCredentialOperationQuery(Guid operationId)
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}
