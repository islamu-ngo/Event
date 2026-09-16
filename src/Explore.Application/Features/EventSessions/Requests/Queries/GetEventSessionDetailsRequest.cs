using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

public sealed record GetEventSessionDetailsRequest(Guid Id = default) : IQuery<EventSessionDto?>;
