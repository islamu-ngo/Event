using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag.Validators;
using Explore.Application.Features.Tags.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Tags.Handlers.Commands;

public class CreateTagCommandHandler : ICommandHandler<CreateTagCommand, BaseCommandResponse<Guid>>
{
    private readonly ITagRepository _tagRepository;
    private readonly ITenantContext _tenantContext;

    public CreateTagCommandHandler(
        ITagRepository tagRepository,
        ITenantContext tenantContext)
    {
        _tagRepository = tagRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateTagCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateTagDtoValidator();
        var validationResult = await validator.ValidateAsync(request.TagDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Tag creation failed.");
        }

        var tag = TagMapper.Create(request.TagDto, _tenantContext.TenantId);

        tag = await _tagRepository.Create(tag);

        return BaseCommandResponse.Success(tag.Id, "Tag created successfully.");
    }
}
