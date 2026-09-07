using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.Location.Validators;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Commands;

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, BaseCommandResponse<Guid>>
{
    private readonly ILocationRepository _locationRepository;
    private readonly IAddressSelectionProtector _selectionProtector;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAddressGovernancePolicyResolver _governancePolicyResolver;
    private readonly TimeProvider _timeProvider;

    public CreateLocationCommandHandler(
        ILocationRepository locationRepository,
        IAddressSelectionProtector selectionProtector,
        ITenantContext tenantContext,
        IUserContext userContext,
        IAddressGovernancePolicyResolver governancePolicyResolver,
        TimeProvider timeProvider)
    {
        _locationRepository = locationRepository;
        _selectionProtector = selectionProtector;
        _tenantContext = tenantContext;
        _userContext = userContext;
        _governancePolicyResolver = governancePolicyResolver;
        _timeProvider = timeProvider;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateLocationCommand request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (request.TenantId != tenantId)
        {
            return BaseCommandResponse.Failure<Guid>(
                "location_tenant_context_mismatch",
                "Location creation failed because the tenant context did not match.");
        }

        var validator = new CreateLocationDtoValidator();
        var validationResult = await validator.ValidateAsync(request.LocationDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Location creation failed.");
        }

        Guid userId;
        AddressGovernancePolicyDecision decision;
        try
        {
            userId = _userContext.GetRequiredUserId();
            decision = await _governancePolicyResolver.ResolveAsync(
                new AddressGovernancePolicyRequest(
                    tenantId,
                    userId,
                    userId,
                    request.LocationDto.OrganizationId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure();
        }

        if (!decision.IsValidManualDecision(
            request.LocationDto.OrganizationId))
        {
            return Failure();
        }

        ProtectedAddressSelection? protectedSelection = null;
        if (!string.IsNullOrWhiteSpace(request.LocationDto.AddressSelectionToken))
        {
            AddressSelectionUnprotectResult unprotectResult;
            try
            {
                unprotectResult = await _selectionProtector.UnprotectAsync(
                    request.LocationDto.AddressSelectionToken,
                    new AddressSelectionContext
                    {
                        TenantId = tenantId,
                        ActorId = userId,
                        OrganizationId = request.LocationDto.OrganizationId,
                        Purpose = AddressSelectionPurpose.CreateLocation,
                        Target = new AddressSelectionTarget(),
                        ConfigurationFingerprint = _selectionProtector.ConfigurationFingerprint
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Failure(FailureCodes.AddressSelectionInvalid);
            }

            if (!unprotectResult.IsSuccess || unprotectResult.Selection is null)
            {
                return Failure(FailureCodes.AddressSelectionInvalid);
            }

            protectedSelection = unprotectResult.Selection;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTime changedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        Location location;
        try
        {
            location = protectedSelection is { } selection
                ? CreateProviderLocation(selection, tenantId, changedAtUtc)
                : CreateManualLocation(request.LocationDto, tenantId, changedAtUtc);
        }
        catch (ArgumentException)
        {
            return Failure(FailureCodes.AddressSelectionInvalid);
        }

        LocationAddressSourceEnum source = protectedSelection is null
            ? LocationAddressSourceEnum.Manual
            : LocationAddressSourceEnum.ProviderSelection;
        location.ApplyAddressGovernanceWithAudit(
            userId,
            source,
            decision.InitialVisibility,
            decision.AddressOrganizationId,
            changedAtUtc);

        cancellationToken.ThrowIfCancellationRequested();
        Location persisted = await _locationRepository.Create(location, cancellationToken);

        return BaseCommandResponse.Success(persisted.Id, "Location created successfully.");
    }

    private static Location CreateManualLocation(
        CreateLocationDto dto,
        Guid tenantId,
        DateTime changedAtUtc)
    {
        var location = new Location
        {
            FullName = dto.FullName,
            Country = dto.Country,
            City = dto.City,
            Timezone = dto.Timezone,
            TenantId = tenantId,
            CreatedAt = changedAtUtc
        };
        location.SetManualAddress(dto.Address, dto.Postcode);
        return location;
    }

    private static Location CreateProviderLocation(
        ProtectedAddressSelection selection,
        Guid tenantId,
        DateTime changedAtUtc)
    {
        var location = new Location
        {
            FullName = selection.DisplayName,
            Country = selection.Country,
            City = selection.City,
            Timezone = selection.Timezone,
            TenantId = tenantId,
            CreatedAt = changedAtUtc
        };
        location.SetProviderAddress(
            selection.Address,
            selection.Postcode,
            GeoCoordinate.Create(selection.Latitude, selection.Longitude));
        return location;
    }

    private static BaseCommandResponse<Guid> Failure(
        string failureCode = "location_address_governance_failed") =>
        BaseCommandResponse.Failure<Guid>(failureCode, "Location creation failed.");
}
