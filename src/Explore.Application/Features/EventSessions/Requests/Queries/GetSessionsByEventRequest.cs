using System;
using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

public sealed record GetSessionsByEventRequest(Guid EventId = default) : IQuery<List<EventSessionListDto>>;
