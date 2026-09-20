using System;
using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

public sealed record GetSessionsByActorQuery : IQuery<List<EventSessionSpeakerListDto>>
{
    public Guid ActorId { get; init; }
}
