using System;
using Explore.Application.DTOs.EventTags;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventTagsDetailsRequest(Guid Id = default) : IRequest<EventTagsDto>;
