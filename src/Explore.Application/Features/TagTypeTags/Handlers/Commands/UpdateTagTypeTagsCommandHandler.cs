using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags.Validators;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Commands;

public class UpdateTagTypeTagsCommandHandler : IRequestHandler<UpdateTagTypeTagsCommand, BaseCommandResponse<Guid>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly ITagRepository _tagRepository;
    private readonly ITagTypeRepository _tagTypeRepository;
    private readonly ITenantContext _tenantContext;

    public UpdateTagTypeTagsCommandHandler(
        ITagTypeTagsRepository repository,
        ITagRepository tagRepository,
        ITagTypeRepository tagTypeRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _tagRepository = tagRepository;
        _tagTypeRepository = tagTypeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateTagTypeTagsCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateTagTypeTagsDtoValidator();
        var validationResult = await validator.ValidateAsync(request.TagTypeTagsDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Tag Type Tags update failed.");
        }

        var link = await _repository.GetById(request.TagTypeTagsId);
        if (link == null || link.TenantId != _tenantContext.TenantId)
        {
            const string message = "Tag Type Tags not found.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        Guid tagId = request.TagTypeTagsDto.Relationship?.TagId ?? link.TagId;
        int tagTypeId = request.TagTypeTagsDto.Relationship?.TagTypeId ?? link.TagTypeId;
        var tag = await _tagRepository.GetById(tagId);
        if (tag is null || tag.TenantId != link.TenantId || !await _tagTypeRepository.Exists(tagTypeId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Relationship targets were not found in the current tenant."],
                "Tag Type Tags update failed.");
        }

        if ((tagId != link.TagId || tagTypeId != link.TagTypeId) && await _repository.Exists(tagId, tagTypeId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Tag and Tag Type relationship already exists."],
                "Tag Type Tags update failed.");
        }

        link.TagId = tagId;
        link.TagTypeId = tagTypeId;
        await _repository.Update(link);

        return BaseCommandResponse.Success(link.Id, "Tag Type Tags updated successfully.");
    }
}
