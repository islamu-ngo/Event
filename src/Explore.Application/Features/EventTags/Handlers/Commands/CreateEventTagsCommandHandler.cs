using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags.Validators;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTags.Handlers.Commands;

public class CreateEventTagsCommandHandler : IRequestHandler<CreateEventTagsCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITagRepository _tagRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;

    public CreateEventTagsCommandHandler(
        IEventTagsRepository eventTagsRepository,
        IEventRepository eventRepository,
        ITagRepository tagRepository,
        ITenantContext tenantContext,
        IMapper mapper)
    {
        _eventTagsRepository = eventTagsRepository;
        _eventRepository = eventRepository;
        _tagRepository = tagRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventTagsCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventTagsDtoValidator(_eventRepository, _tagRepository, _eventTagsRepository);
        var validationResult = await validator.ValidateAsync(request.EventTagsDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event Tag assignment failed.");
        }

        var eventTags = _mapper.Map<Domain.EventTags>(request.EventTagsDto);

        // Set TenantId from the request context
        eventTags.TenantId = _tenantContext.TenantId;

        eventTags = await _eventTagsRepository.Create(eventTags);

        return BaseCommandResponse.Success(eventTags.Id, "Event Tag assigned successfully.");
    }
}
