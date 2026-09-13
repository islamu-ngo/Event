using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Event;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventsByTagRequest(Guid TagId = default) : IQuery<List<EventListDto>>;
