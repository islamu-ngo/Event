using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Language;
using Explore.Application.Features.Languages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Languages.Handlers.Queries;

public class GetLanguageListRequestHandler : IRequestHandler<GetLanguageListRequest, List<LanguageListDto>>
{
    private readonly ILanguageRepository _languageRepository;

    public GetLanguageListRequestHandler(
        ILanguageRepository languageRepository)
    {
        _languageRepository = languageRepository;
    }

    public async Task<List<LanguageListDto>> Handle(GetLanguageListRequest request, CancellationToken cancellationToken)
    {
        var languages = await _languageRepository.GetAll();
        return languages.Select(LanguageMapper.ToListItem).ToList();
    }
}
