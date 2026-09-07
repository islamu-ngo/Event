// ABOUTME: Requests a bounded immutable page of Local identity administration summaries.
// ABOUTME: Leaves administrator resolution and metadata eligibility to the query handler and native reader.

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
