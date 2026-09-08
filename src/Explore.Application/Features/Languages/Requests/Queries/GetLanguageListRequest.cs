using System.Collections.Generic;
using Explore.Application.DTOs.Language;
using MediatR;

namespace Explore.Application.Features.Languages.Requests.Queries;

public sealed record GetLanguageListRequest : IRequest<List<LanguageListDto>>
{
}
