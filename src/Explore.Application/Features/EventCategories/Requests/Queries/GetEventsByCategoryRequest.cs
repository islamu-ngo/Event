using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Event;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetEventsByCategoryRequest(Guid CategoryId) : IQuery<List<EventListDto>>;
