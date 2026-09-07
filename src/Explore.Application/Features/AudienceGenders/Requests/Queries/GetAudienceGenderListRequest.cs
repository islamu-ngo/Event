using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.DTOs.AudienceGender;
using MediatR;

namespace Explore.Application.Features.AudienceGenders.Requests.Queries;

public sealed record GetAudienceGenderListRequest : IRequest<List<AudienceGenderListDto>>
{
}
