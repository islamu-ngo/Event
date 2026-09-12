using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.DTOs.StatusType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StatusTypes.Requests.Queries;

public sealed record GetStatusTypeListRequest : IQuery<List<StatusTypeListDto>>
{
}
