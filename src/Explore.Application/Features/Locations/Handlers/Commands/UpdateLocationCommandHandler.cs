// ABOUTME: Handler for grouped Location PATCH updates with optimistic concurrency.
// ABOUTME: Applies manual address changes atomically and clears any stale provider coordinate.

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.Location.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Commands;

public class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, BaseCommandResponse<Guid>>
{
    private readonly ILocationRepository _locationRepository;
    private readonly IAddressSelectionProtector _selectionProtector;
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAddressGovernancePolicyResolver _governancePolicyResolver;
    private readonly TimeProvider _timeProvider;

    public UpdateLocationCommandHandler(
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

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateLocationDtoValidator();
        var validationResult = await validator.ValidateAsync(request.UpdateLocationDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Location update failed.");
        }

        Guid tenantId = _tenantContext.TenantId;
        Location? location = await _locationRepository.GetById(request.LocationId, cancellationToken);

        if (location is null || location.TenantId != tenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Location not found.");
        }

        if (location.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The location was modified by another request. Reload and retry.",
                nameof(Location));
        }

        bool hasProtectedSelection =
            !string.IsNullOrWhiteSpace(request.UpdateLocationDto.AddressSelectionToken);
        (string Address, string Postcode)? manualBundle = hasProtectedSelection
            ? null
            : ResolveManualBundle(
                location,
                request.UpdateLocationDto.Address,
                request.UpdateLocationDto.Postcode);
        bool addressChanges = hasProtectedSelection || (manualBundle is { } bundle
            && (!string.Equals(location.Address, bundle.Address, StringComparison.Ordinal)
                || !string.Equals(location.Postcode, bundle.Postcode, StringComparison.Ordinal)
                || location.GetCoordinate() is not null));
        Guid? userId = null;
        AddressGovernancePolicyDecision? decision = null;
        if (addressChanges)
        {
            try
            {
                userId = _userContext.GetRequiredUserId();
                decision = await _governancePolicyResolver.ResolveAsync(
                    new AddressGovernancePolicyRequest(
                        tenantId,
                        userId,
                        userId,
                        request.UpdateLocationDto.OrganizationId),
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
                request.UpdateLocationDto.OrganizationId))
            {
                return Failure();
            }
        }

        ProtectedAddressSelection? protectedSelection = null;
        if (hasProtectedSelection && userId.HasValue)
        {
            AddressSelectionUnprotectResult unprotectResult;
            try
            {
                unprotectResult = await _selectionProtector.UnprotectAsync(
                    request.UpdateLocationDto.AddressSelectionToken!,
                    new AddressSelectionContext
                    {
                        TenantId = tenantId,
                        ActorId = userId.Value,
                        OrganizationId = request.UpdateLocationDto.OrganizationId,
                        Purpose = AddressSelectionPurpose.UpdateLocation,
                        Target = new AddressSelectionTarget
                        {
                            LocationId = request.LocationId,
                            ExpectedConcurrencyStamp = request.ExpectedConcurrencyStamp
                        },
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

        // Validate the complete write before changing any tracked aggregate state.
        try
        {
            _ = LocationTextNormalization.Normalize(protectedSelection?.DisplayName
                ?? request.UpdateLocationDto.FullName?.Value ?? location.FullName);
            if (protectedSelection is { } selected)
            {
                _ = LocationTextNormalization.Normalize(selected.Address);
                ArgumentException.ThrowIfNullOrWhiteSpace(selected.Postcode);
                _ = GeoCoordinate.Create(selected.Latitude, selected.Longitude);
            }
            else if (manualBundle is { } proposed)
            {
                _ = LocationTextNormalization.Normalize(proposed.Address);
                ArgumentException.ThrowIfNullOrWhiteSpace(proposed.Postcode);
            }
        }
        catch (ArgumentException)
        {
            return Failure(FailureCodes.AddressSelectionInvalid);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ApplyFullName(location, request.UpdateLocationDto.FullName);
        ApplyCountry(location, request.UpdateLocationDto.Country);
        ApplyCity(location, request.UpdateLocationDto.City);
        if (addressChanges && decision is not null && userId.HasValue)
        {
            DateTime changedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            try
            {
                if (protectedSelection is { } selection)
                {
                    ApplyProviderSelection(location, selection);
                    location.ApplyAddressGovernanceWithAudit(
                        userId.Value,
                        LocationAddressSourceEnum.ProviderSelection,
                        decision.InitialVisibility,
                        decision.AddressOrganizationId,
                        changedAtUtc);
                }
                else if (manualBundle is { } changedBundle)
                {
                    location.SetManualAddress(changedBundle.Address, changedBundle.Postcode);
                    location.ApplyAddressGovernanceWithAudit(
                        userId.Value,
                        LocationAddressSourceEnum.Manual,
                        decision.InitialVisibility,
                        decision.AddressOrganizationId,
                        changedAtUtc);
                }
            }
            catch (ArgumentException)
            {
                return Failure(FailureCodes.AddressSelectionInvalid);
            }
        }
        ApplyTimezone(location, request.UpdateLocationDto.Timezone);

        bool hasNonAddressPatch = request.UpdateLocationDto.FullName is not null
            || request.UpdateLocationDto.Country is not null
            || request.UpdateLocationDto.City is not null
            || request.UpdateLocationDto.Timezone is not null;
        cancellationToken.ThrowIfCancellationRequested();
        if (addressChanges || hasNonAddressPatch)
        {
            await _locationRepository.Update(location, cancellationToken);
        }

        return BaseCommandResponse.Success(location.Id, "Location updated successfully.");
    }

    private static void ApplyFullName(Location location, UpdateLocationFullNameDto? group)
    {
        if (group is not null)
        {
            location.FullName = group.Value;
        }
    }

    private static (string Address, string Postcode)? ResolveManualBundle(
        Location location,
        UpdateLocationAddressDto? address,
        UpdateLocationPostcodeDto? postcode)
    {
        if (address is null && postcode is null)
        {
            return null;
        }

        string effectiveAddress = address?.Value
            ?? location.Address
            ?? throw new InvalidOperationException("An address-only Location update requires existing address PII.");
        string effectivePostcode = postcode?.Value
            ?? location.Postcode
            ?? throw new InvalidOperationException("A postcode-only Location update requires existing postcode PII.");

        return (effectiveAddress, effectivePostcode);
    }

    private static void ApplyCountry(Location location, UpdateLocationCountryDto? group)
    {
        if (group is not null)
        {
            location.Country = group.Value;
        }
    }

    private static void ApplyCity(Location location, UpdateLocationCityDto? group)
    {
        if (group is not null)
        {
            location.City = group.Value;
        }
    }

    private static void ApplyProviderSelection(
        Location location,
        ProtectedAddressSelection selection)
    {
        location.FullName = selection.DisplayName;
        location.Country = selection.Country;
        location.City = selection.City;
        location.Timezone = selection.Timezone;
        location.SetProviderAddress(
            selection.Address,
            selection.Postcode,
            GeoCoordinate.Create(selection.Latitude, selection.Longitude));
    }

    private static BaseCommandResponse<Guid> Failure(
        string failureCode = "location_address_governance_failed") =>
        BaseCommandResponse.Failure<Guid>(failureCode, "Location update failed.");

    private static void ApplyTimezone(Location location, UpdateLocationTimezoneDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            location.Timezone = group.Value.Value;
        }
    }
}
