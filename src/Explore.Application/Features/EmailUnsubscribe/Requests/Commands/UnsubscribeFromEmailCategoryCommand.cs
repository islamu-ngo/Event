namespace Explore.Application.Features.EmailUnsubscribe.Requests.Commands;

using Explore.Application.Responses;
using MediatR;

public sealed record UnsubscribeFromEmailCategoryCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public required string Category { get; init; }
}
