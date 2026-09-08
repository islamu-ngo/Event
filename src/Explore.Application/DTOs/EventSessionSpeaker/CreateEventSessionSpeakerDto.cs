using System;

namespace Explore.Application.DTOs.EventSessionSpeaker;

public sealed record CreateEventSessionSpeakerDto
{
    public Guid ActorId { get; init; }
    public Guid EventSessionId { get; init; }
}
