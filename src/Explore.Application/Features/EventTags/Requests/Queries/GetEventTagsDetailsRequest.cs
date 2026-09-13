using System;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventTagsDetailsRequest(Guid Id = default) : IQuery<EventTagsDto>;
