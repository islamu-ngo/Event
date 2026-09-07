using System.Collections.Generic;
using Explore.Application.DTOs.EventTags;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventTagsListRequest : IRequest<List<EventTagsListDto>>
{
}
