using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Language;
using Explore.Application.Features.Languages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Languages.Handlers.Queries;

public class GetLanguageDetailsRequestHandler : IRequestHandler<GetLanguageDetailsRequest, LanguageDto>
{
    private readonly ILanguageRepository _languageRepository;

    public GetLanguageDetailsRequestHandler(
        ILanguageRepository languageRepository)
    {
        _languageRepository = languageRepository;
    }

    public async Task<LanguageDto> Handle(GetLanguageDetailsRequest request, CancellationToken cancellationToken)
    {
        var language = await _languageRepository.GetById(request.Id);
        return LanguageMapper.ToDetail(language)!;
    }
}
