using System;
using Explore.Application.DTOs.Category;
using MediatR;

namespace Explore.Application.Features.Categories.Requests.Queries;

public sealed record GetCategoryDetailsRequest(Guid Id = default) : IRequest<CategoryDto>;
