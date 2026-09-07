using System.Collections.Generic;
using Explore.Application.DTOs.EventCategories;
using MediatR;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetEventCategoriesListRequest : IRequest<List<EventCategoriesListDto>>
{
}
