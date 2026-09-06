// ABOUTME: Executes bounded local address search using trusted tenant and user context.
// ABOUTME: Maps the persistence projection to a private provider-neutral Application contract.

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Requests.Queries;
using Explore.Application.Features.Geocoding.Validators;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Geocoding.Handlers.Queries;

public sealed class GetAddressSuggestionsQueryHandler(
    ILocalAddressSuggestionQuery localQuery,
    IAddressSuggestionProviderGateway providerGateway,
    IAddressSelectionProtector selectionProtector,
    ITenantContext tenantContext,
    IUserContext userContext)
    : IRequestHandler<GetAddressSuggestionsQuery, AddressSuggestionsResponseDto>
{
    public async Task<AddressSuggestionsResponseDto> Handle(
        GetAddressSuggestionsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validator = new GetAddressSuggestionsQueryValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        try
        {
            _ = LocationTextNormalization.Normalize(request.Request.SearchText.Trim());
        }
        catch (ArgumentException)
        {
            throw new ValidationException("Address suggestion search text is invalid.");
        }

        Guid tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty || request.TenantId != tenantId)
        {
            throw new ValidationException("Address suggestion tenant context is invalid.");
        }

        Guid userId = userContext.GetRequiredUserId();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LocalAddressSuggestion> suggestions = await localQuery.SearchAsync(
            new LocalAddressSuggestionCriteria(
                tenantId,
                userId,
                userId,
                request.Request.OrganizationId,
                request.Request.SearchText.Trim(),
                request.Request.Limit),
            cancellationToken);

        AddressSuggestionDto[] localSuggestions = suggestions
            .Select(suggestion => new AddressSuggestionDto(
                suggestion.LocationId,
                suggestion.ConcurrencyStamp,
                suggestion.DisplayName,
                suggestion.Address,
                suggestion.Postcode,
                suggestion.Source,
                suggestion.Visibility,
                City: suggestion.City,
                Country: suggestion.Country,
                Timezone: suggestion.Timezone)
            {
                TenantId = tenantId
            })
            .ToArray();

        int remaining = request.Request.Limit - localSuggestions.Length;
        if (remaining <= 0)
        {
            return new(localSuggestions, AddressProviderOutcome.None);
        }

        AddressGeocoderResult providerResult;
        try
        {
            providerResult = await providerGateway.SearchAsync(
                new AddressGeocoderRequest(request.Request.SearchText.Trim(), remaining),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(localSuggestions, AddressProviderOutcome.Unavailable);
        }

        if (providerResult.Outcome is not AddressProviderOutcome.Ready
            and not AddressProviderOutcome.Limited)
        {
            return new(localSuggestions, providerResult.Outcome);
        }

        AddressSelectionContext selectionContext = CreateSelectionContext(
            request.Request,
            tenantId,
            userId,
            selectionProtector.ConfigurationFingerprint);
        var protectedSuggestions = new List<AddressSuggestionDto>(
            Math.Min(providerResult.Selections.Count, remaining));

        try
        {
            foreach (ProtectedAddressSelection selection in providerResult.Selections.Take(remaining))
            {
                AddressSelectionToken token = await selectionProtector.ProtectAsync(
                    selection,
                    selectionContext,
                    cancellationToken);
                protectedSuggestions.Add(new AddressSuggestionDto(
                    LocationId: null,
                    ConcurrencyStamp: null,
                    selection.DisplayName,
                    selection.Address,
                    selection.Postcode,
                    LocationAddressSourceEnum.ProviderSelection,
                    LocationAddressVisibilityEnum.Quarantined,
                    selection.Attribution,
                    token.Value,
                    token.ExpiresAt,
                    selection.City,
                    selection.Country,
                    selection.Timezone)
                {
                    TenantId = tenantId
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(localSuggestions, providerResult.Outcome);
        }

        return new(
            [.. localSuggestions, .. protectedSuggestions],
            providerResult.Outcome);
    }

    private static AddressSelectionContext CreateSelectionContext(
        AddressSuggestionsRequestDto request,
        Guid tenantId,
        Guid actorId,
        string configurationFingerprint) => new()
        {
            TenantId = tenantId,
            ActorId = actorId,
            OrganizationId = request.OrganizationId,
            Purpose = request.LocationId.HasValue
                ? AddressSelectionPurpose.UpdateLocation
                : AddressSelectionPurpose.CreateLocation,
            Target = new AddressSelectionTarget
            {
                LocationId = request.LocationId,
                ExpectedConcurrencyStamp = request.ExpectedConcurrencyStamp
            },
            ConfigurationFingerprint = configurationFingerprint
        };
}
