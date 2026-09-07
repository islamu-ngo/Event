using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Actor.Validators;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public class CreateActorCommandHandler : IRequestHandler<CreateActorCommand, BaseCommandResponse<Guid>>
{
    private readonly IActorRepository _actorRepository;
    private readonly IActorTypeRepository _actorTypeRepository;
    private readonly IDidCustodyTypeRepository _didCustodyTypeRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;

    public CreateActorCommandHandler(
        IActorRepository actorRepository,
        IActorTypeRepository actorTypeRepository,
        IDidCustodyTypeRepository didCustodyTypeRepository,
        IStorageObjectRepository storageObjectRepository,
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        ITenantContext tenantContext,
        IMapper mapper)
    {
        _actorRepository = actorRepository;
        _actorTypeRepository = actorTypeRepository;
        _didCustodyTypeRepository = didCustodyTypeRepository;
        _storageObjectRepository = storageObjectRepository;
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateActorCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateActorDtoValidator(
            _actorTypeRepository,
            _didCustodyTypeRepository,
            _storageObjectRepository,
            _tenantRepository,
            _actorRepository,
            _userRepository,
            _organizationRepository);

        var validationResult = await validator.ValidateAsync(request.ActorDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Actor creation failed.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                _tenantContext.TenantId,
                request.ActorDto.ProfilePictureId,
                request.ActorDto.BannerPictureId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Every image must be an active public safe-raster object in the current tenant."],
                "Actor creation failed.");
        }

        var actor = _mapper.Map<Actor>(request.ActorDto);

        actor = await _actorRepository.Create(actor);

        return BaseCommandResponse.Success(actor.Id, "Actor created successfully.");
    }
}
