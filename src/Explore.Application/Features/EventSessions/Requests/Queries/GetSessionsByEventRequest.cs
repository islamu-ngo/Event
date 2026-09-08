using System;
using System.Collections.Generic;
using Explore.Application.DTOs.EventSession;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

public sealed record GetSessionsByEventRequest(Guid EventId = default) : IRequest<List<EventSessionListDto>>;
