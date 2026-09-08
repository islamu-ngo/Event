using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Category;
using MediatR;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetCategoriesByEventRequest(Guid EventId) : IRequest<List<CategoryListDto>>;
