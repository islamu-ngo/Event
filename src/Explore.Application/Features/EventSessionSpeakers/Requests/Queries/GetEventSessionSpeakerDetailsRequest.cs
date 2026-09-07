using System;
using Explore.Application.DTOs.EventSessionSpeaker;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

public sealed record GetEventSessionSpeakerDetailsRequest(Guid Id = default) : IRequest<EventSessionSpeakerDto>;
