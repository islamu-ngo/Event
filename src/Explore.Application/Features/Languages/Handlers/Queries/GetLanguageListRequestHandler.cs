using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Language;
using Explore.Application.Features.Languages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Languages.Handlers.Queries;

public class GetLanguageListRequestHandler : IRequestHandler<GetLanguageListRequest, List<LanguageListDto>>
{
    private readonly ILanguageRepository _languageRepository;
    private readonly IMapper _mapper;

    public GetLanguageListRequestHandler(
        ILanguageRepository languageRepository,
        IMapper mapper)
    {
        _languageRepository = languageRepository;
        _mapper = mapper;
    }

    public async Task<List<LanguageListDto>> Handle(GetLanguageListRequest request, CancellationToken cancellationToken)
    {
        var languages = await _languageRepository.GetAll();
        return _mapper.Map<List<LanguageListDto>>(languages);
    }
}
