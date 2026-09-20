using System;
using Explore.Application.DTOs.Category;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Categories.Requests.Queries;

public sealed record GetCategoryDetailsRequest(Guid Id = default) : IQuery<CategoryDto?>;
