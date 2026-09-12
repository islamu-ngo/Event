using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceGenders.Requests.Queries;

public sealed record GetAudienceGenderListRequest : IQuery<List<AudienceGenderListDto>>
{
}
