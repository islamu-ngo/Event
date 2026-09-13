using System;
using System.Collections.Generic;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record GetSpeakersBySessionQuery : IQuery<List<EventSessionSpeakerListDto>>, ISecureRequest
{
    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
