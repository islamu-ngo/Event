using System;
using Explore.Application.DTOs.EventSession;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

public sealed record GetEventSessionDetailsRequest(Guid Id = default) : IRequest<EventSessionDto?>;
