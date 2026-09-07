using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Language;
using Explore.Application.Features.Languages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Languages.Handlers.Queries;

public class GetLanguageDetailsRequestHandler : IRequestHandler<GetLanguageDetailsRequest, LanguageDto>
{
    private readonly ILanguageRepository _languageRepository;
    private readonly IMapper _mapper;

    public GetLanguageDetailsRequestHandler(
        ILanguageRepository languageRepository,
        IMapper mapper)
    {
        _languageRepository = languageRepository;
        _mapper = mapper;
    }

    public async Task<LanguageDto> Handle(GetLanguageDetailsRequest request, CancellationToken cancellationToken)
    {
        var language = await _languageRepository.GetById(request.Id);
        return _mapper.Map<LanguageDto>(language);
    }
}
