using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags.Validators;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Commands;

public class CreateTagTypeTagsCommandHandler : IRequestHandler<CreateTagTypeTagsCommand, BaseCommandResponse<Guid>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;
    private readonly ITagRepository _tagRepository;
    private readonly ITagTypeRepository _tagTypeRepository;
    private readonly ITenantContext _tenantContext;

    public CreateTagTypeTagsCommandHandler(
        ITagTypeTagsRepository repository,
        IMapper mapper,
        ITagRepository tagRepository,
        ITagTypeRepository tagTypeRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _mapper = mapper;
        _tagRepository = tagRepository;
        _tagTypeRepository = tagTypeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateTagTypeTagsCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateTagTypeTagsDtoValidator(_tagRepository, _tagTypeRepository, _repository);
        var validationResult = await validator.ValidateAsync(request.TagTypeTagsDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Tag Type Tags creation failed.");
        }

        var tagTypeTags = _mapper.Map<Domain.TagTypeTags>(request.TagTypeTagsDto);

        // Set TenantId from request context
        tagTypeTags.TenantId = _tenantContext.TenantId;

        tagTypeTags = await _repository.Create(tagTypeTags);

        return BaseCommandResponse.Success(tagTypeTags.Id, "Tag Type Tags created successfully.");
    }
}
