using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag.Validators;
using Explore.Application.Features.Tags.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Tags.Handlers.Commands;

public class CreateTagCommandHandler : IRequestHandler<CreateTagCommand, BaseCommandResponse<Guid>>
{
    private readonly ITagRepository _tagRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;

    public CreateTagCommandHandler(
        ITagRepository tagRepository,
        ITenantContext tenantContext,
        IMapper mapper)
    {
        _tagRepository = tagRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateTagCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateTagDtoValidator();
        var validationResult = await validator.ValidateAsync(request.TagDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Tag creation failed.");
        }

        var tag = _mapper.Map<Tag>(request.TagDto);

        // Set TenantId from the request context
        tag.TenantId = _tenantContext.TenantId;

        tag = await _tagRepository.Create(tag);

        return BaseCommandResponse.Success(tag.Id, "Tag created successfully.");
    }
}
