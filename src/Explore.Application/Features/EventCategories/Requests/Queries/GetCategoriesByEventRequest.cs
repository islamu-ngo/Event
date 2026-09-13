using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Category;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetCategoriesByEventRequest(Guid EventId) : IQuery<List<CategoryListDto>>;
