using System;
using System.Collections.Generic;
using Explore.Application.DTOs.EventSessionSpeaker;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

public sealed record GetSessionsByActorRequest : IRequest<List<EventSessionSpeakerListDto>>
{
    public Guid ActorId { get; init; }
}
