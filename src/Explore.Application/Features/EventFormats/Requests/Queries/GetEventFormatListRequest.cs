using System.Collections.Generic;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventFormats.Requests.Queries;

public sealed record GetEventFormatListRequest : IQuery<List<EventFormatListDto>>
{
}
