using System.Collections.Generic;
using Explore.Application.DTOs.Language;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Languages.Requests.Queries;

public sealed record GetLanguageListRequest : IQuery<List<LanguageListDto>>
{
}
