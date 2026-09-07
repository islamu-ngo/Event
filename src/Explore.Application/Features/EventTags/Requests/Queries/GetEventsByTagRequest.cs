using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Event;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventsByTagRequest(Guid TagId = default) : IRequest<List<EventListDto>>;
