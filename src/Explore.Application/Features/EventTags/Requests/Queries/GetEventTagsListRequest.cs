using System.Collections.Generic;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetEventTagsListRequest : IQuery<List<EventTagsListDto>>
{
}
