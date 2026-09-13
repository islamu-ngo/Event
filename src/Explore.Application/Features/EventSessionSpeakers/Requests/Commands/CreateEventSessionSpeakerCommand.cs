using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record CreateEventSessionSpeakerCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionSpeakerDto SpeakerDto { get; init; }

    string? ISecureRequest.ResourceId => SpeakerDto.EventSessionId.ToString();
}
