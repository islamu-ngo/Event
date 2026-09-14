using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Users.Requests.Queries;

public sealed record CheckUserExistsQuery : IQuery<bool>
{
    public required string Email { get; init; } = string.Empty;
}
