using System;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetEventCategoriesDetailsRequest(Guid Id) : IQuery<EventCategoriesDto>;
