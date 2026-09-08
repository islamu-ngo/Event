
using Explore.Application.Contracts.Identity;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Queries;

public sealed record ListLocalIdentitiesQuery : IRequest<LocalIdentityPage>
{
    public const string ResourceKey = "local-identities";

    public ListLocalIdentitiesQuery(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public int PageNumber { get; }
    public int PageSize { get; }
}
