using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage.Validators;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Commands;

public class CreateEventSessionLanguageCommandHandler : IRequestHandler<CreateEventSessionLanguageCommand, BaseCommandResponse<int>>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IMapper _mapper;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ILanguageRepository _languageRepository;
    private readonly ITenantContext _tenantContext;

    public CreateEventSessionLanguageCommandHandler(
        IEventSessionLanguageRepository repository,
        IMapper mapper,
        IEventSessionRepository eventSessionRepository,
        ILanguageRepository languageRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _mapper = mapper;
        _eventSessionRepository = eventSessionRepository;
        _languageRepository = languageRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<int>> Handle(CreateEventSessionLanguageCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionLanguageDtoValidator(_eventSessionRepository, _languageRepository);
        var validationResult = await validator.ValidateAsync(request.EventSessionLanguageDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<int>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event Session Language creation failed.");
        }

        var eventSessionLanguage = _mapper.Map<EventSessionLanguage>(request.EventSessionLanguageDto);

        // Set TenantId from the request context
        eventSessionLanguage.TenantId = _tenantContext.TenantId;

        eventSessionLanguage = await _repository.Create(eventSessionLanguage);

        return BaseCommandResponse.Success(
            eventSessionLanguage.Id,
            "Event Session Language created successfully.");
    }
}
