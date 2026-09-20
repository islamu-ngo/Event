using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Users.Requests.Queries;

public sealed record ResolveCurrentUserIdByIdentityRequest : IQuery<Guid?>
{
    public required string Provider { get; init; }
    public required string ProviderId { get; init; }
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
}
