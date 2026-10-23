using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.LocationRoom.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.LocationRooms.Handlers.Commands;

public class UpdateLocationRoomCommandHandler : ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipal;
    private readonly IMachineMutationCommitAuthority _machineAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly RequestAuthorization<UpdateLocationRoomCommand> _authorization;
    private readonly ILogger<UpdateLocationRoomCommandHandler> _logger;

    public UpdateLocationRoomCommandHandler(
        ILocationRoomRepository locationRoomRepository,
        ILocationRepository locationRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        IMachinePrincipalAccessor machinePrincipal, IMachineMutationCommitAuthority machineAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority, ITenantAdministratorCommitAuthority tenantAuthority,
        RequestAuthorization<UpdateLocationRoomCommand> authorization,
        ILogger<UpdateLocationRoomCommandHandler> logger,
        ISerializableAttemptOwner owner,
        IHumanAdministrativeActionPreparation preparation)
    {
        _locationRoomRepository = locationRoomRepository;
        _locationRepository = locationRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _userContext = userContext;
        _adminContext = adminContext;
        _machinePrincipal = machinePrincipal;
        _machineAuthority = machineAuthority;
        _instanceAuthority = instanceAuthority;
        _tenantAuthority = tenantAuthority;
        _authorization = authorization;
        _logger = logger;
        _owner = owner;
        _preparation = preparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateLocationRoomCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        Guid? humanId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        bool machine = _machinePrincipal.IsMachineCaller;
        if (!_userContext.IsAuthenticated || tenantId == Guid.Empty || !machine && humanId is null)
            return BaseCommandResponse.Authentication<Guid>();
        var validator = new UpdateLocationRoomDtoValidator(_locationRepository);
        var validationResult = await validator.ValidateAsync(request.UpdateLocationRoomDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Room update failed.");
        }

        var prepared = machine ? null
            : await _preparation.PrepareAsync(request, humanId!.Value, cancellationToken);
        async Task<BaseCommandResponse<Guid>> ExecuteBodyAsync(CancellationToken transactionToken)
        {
            if (prepared is not null)
                await prepared.AcquireAsync(request, humanId!.Value, transactionToken);
            await _authorization.AuthorizeAsync(request, _logger, transactionToken);
            if (!machine && (humanId is not { } actor
                || !await _instanceAuthority.IsInstanceAdministratorForCommitAsync(actor, transactionToken)
                    && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(actor, tenantId, transactionToken)))
                throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
            Guid? destinationId = request.UpdateLocationRoomDto.Location?.LocationId;
            var room = machine
                ? await _machineAuthority.GetAuthorizedRoomForUpdateAsync(tenantId, request.LocationRoomId, destinationId, transactionToken)
                : await _locationRoomRepository.GetForMutationAsync(request.LocationRoomId, destinationId, transactionToken);
            if (room is null)
                throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
            if (room.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The location room was modified by another request. Reload and retry.", nameof(LocationRoom), room.Id.ToString());
            Location? targetLocation = null;
            if (destinationId is { } destination && destination != room.LocationId)
            {
                targetLocation = await _locationRepository.GetById(destination);
                if (targetLocation is null || targetLocation.TenantId != tenantId)
                    throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
            }
            if (targetLocation is not null)
            {
                if (await _locationRoomRepository.HasScheduleReferencesAsync(room.Id, transactionToken))
                {
                    return BaseCommandResponse.Validation<Guid>(
                        ["A room used by an event schedule cannot be moved to another location."],
                        "A room used by an event schedule cannot be moved to another location.");
                }

                if (machine && await _machineAuthority.GetAuthorizedRoomForUpdateAsync(
                        tenantId, room.Id, destinationId, transactionToken) is null)
                    throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
                await _locationRoomRepository.MoveToLocationAsync(
                    room,
                    targetLocation,
                    request.UpdateLocationRoomDto.Name?.Value ?? room.Name,
                    request.ExpectedConcurrencyStamp,
                    transactionToken);
            }

            ApplyName(room, request.UpdateLocationRoomDto.Name);
            ApplySlug(room, request.UpdateLocationRoomDto.Slug);
            ApplyDescription(room, request.UpdateLocationRoomDto.Description);
            ApplyCapacity(room, request.UpdateLocationRoomDto.Capacity);
            ApplySortOrder(room, request.UpdateLocationRoomDto.SortOrder);

            if (machine && await _machineAuthority.GetAuthorizedRoomForUpdateAsync(
                    tenantId, room.Id, destinationId, transactionToken) is null)
                throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
            await _locationRoomRepository.Update(room);
            if (machine && await _machineAuthority.GetAuthorizedRoomForUpdateAsync(
                    tenantId, room.Id, destinationId, transactionToken) is null)
                throw new AuthorizationException(ResourceKinds.LocationRoom, AuthorizationActions.Update);
            return BaseCommandResponse.Success(room.Id, "Room updated successfully.");
        }
        return machine
            ? await _unitOfWork.ExecuteSerializableAsync(ExecuteBodyAsync, cancellationToken)
            : await _owner.ExecuteAsync(ExecuteBodyAsync, cancellationToken);
    }

    private static void ApplyName(LocationRoom room, UpdateLocationRoomNameDto? group)
    {
        if (group is not null)
        {
            room.Name = group.Value;
        }
    }

    private static void ApplySlug(LocationRoom room, UpdateLocationRoomSlugDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Slug = group.Value.Value;
        }
    }

    private static void ApplyDescription(LocationRoom room, UpdateLocationRoomDescriptionDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Description = group.Value.Value;
        }
    }

    private static void ApplyCapacity(LocationRoom room, UpdateLocationRoomCapacityDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Capacity = group.Value.Value;
        }
    }

    private static void ApplySortOrder(LocationRoom room, UpdateLocationRoomSortOrderDto? group)
    {
        if (group is not null)
        {
            room.SortOrder = group.Value;
        }
    }
}
