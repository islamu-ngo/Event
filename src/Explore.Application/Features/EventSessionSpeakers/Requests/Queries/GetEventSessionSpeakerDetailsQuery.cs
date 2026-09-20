using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

public sealed record GetEventSessionSpeakerDetailsQuery(Guid Id = default) : IQuery<EventSessionSpeakerDto?>;
