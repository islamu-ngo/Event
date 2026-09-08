using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record DeleteEventSessionLanguageCommand : IRequest<bool>, ISecureRequest
{
    public int Id { get; init; }

    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();

}
