using System.Linq;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Users.Handlers.Commands;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, BaseCommandResponse<Guid>>
{
    private readonly IUserRepository _userRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IPrivacyErasureStateRepository _privacyErasureStateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public UpdateUserCommandHandler(
        IUserRepository userRepository,
        IActorRepository actorRepository,
        IStorageObjectRepository storageObjectRepository,
        IPrivacyErasureStateRepository privacyErasureStateRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        HybridCache cache)
    {
        _userRepository = userRepository;
        _actorRepository = actorRepository;
        _storageObjectRepository = storageObjectRepository;
        _privacyErasureStateRepository = privacyErasureStateRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateUserDtoValidator();
        var validationResult = await validator.ValidateAsync(request.UpdateUserDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "User update failed due to validation errors.");
        }

        var transactionResponse = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await _privacyErasureStateRepository.GetBySubjectAsync(request.UserId, token) is not null)
            {
                return BaseCommandResponse.NotFound<Guid>("User not found");
            }

            var user = await _userRepository.GetById(request.UserId);

            if (user == null)
            {
                return BaseCommandResponse.NotFound<Guid>("User not found");
            }

            if (user.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The user was modified by another request. Reload and retry.",
                    nameof(Domain.User),
                    user.Id.ToString());
            }

            // Update names (FirstName/LastName) when provided.
            if (request.UpdateUserDto.Names is not null)
            {
                _mapper.Map(request.UpdateUserDto.Names, user);
            }

            // Update profile picture and link the storage object when provided.
            if (request.UpdateUserDto.ProfileImage is not null)
            {
                var actor = await _actorRepository.GetActorByUserId(user.Id);
                if (actor != null)
                {
                    var storageObject = await _storageObjectRepository.GetById(request.UpdateUserDto.ProfileImage.ProfilePictureId);
                    if (!SafeRasterContentPolicy.IsEligibleImageReference(storageObject, _tenantContext.TenantId))
                    {
                        return BaseCommandResponse.Validation<Guid>(
                            ["Profile image must be an active public safe-raster object in the current tenant."],
                            "Profile image must be an active public safe-raster object in the current tenant.");
                    }

                    if (storageObject != null)
                    {
                        storageObject.ActorId = actor.Id;
                        actor.ProfilePictureUri = storageObject.Uri;
                    }
                }
            }

            await _userRepository.Update(user);

            return BaseCommandResponse.Success(user.Id, "User updated successfully");
        }, cancellationToken);

        if (transactionResponse.IsSuccess)
        {
            await _cache.RemoveAsync($"user:detail:{transactionResponse.Id}", cancellationToken);
        }

        return transactionResponse;
    }
}
