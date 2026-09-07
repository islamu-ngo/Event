using System;
using Explore.Application.DTOs.EventCategories;
using MediatR;

namespace Explore.Application.Features.EventCategories.Requests.Queries;

public sealed record GetEventCategoriesDetailsRequest(Guid Id) : IRequest<EventCategoriesDto>;
