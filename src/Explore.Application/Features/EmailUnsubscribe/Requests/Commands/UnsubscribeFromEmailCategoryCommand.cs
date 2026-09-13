namespace Explore.Application.Features.EmailUnsubscribe.Requests.Commands;

using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

public sealed record UnsubscribeFromEmailCategoryCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public required string Category { get; init; }
}
