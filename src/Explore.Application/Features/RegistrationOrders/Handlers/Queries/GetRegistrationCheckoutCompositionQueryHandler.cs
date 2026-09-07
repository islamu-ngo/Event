using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRegistrationCheckoutCompositionQueryHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    IPlatformFeePolicyRepository feePolicies,
    ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness,
    IOrganizerEarningsCalculator earningsCalculator)
    : IRequestHandler<GetRegistrationCheckoutCompositionQuery, RegistrationCheckoutCompositionDto?>
{
    public async Task<RegistrationCheckoutCompositionDto?> Handle(
        GetRegistrationCheckoutCompositionQuery request,
        CancellationToken cancellationToken)
    {
        var @event = await events.GetById(request.EventId);
        if (@event is null
            || @event.EventStatusId != (int)EventStatusEnum.Published
            || @event.VisibilityTypeId != (int)VisibilityTypeEnum.Public
            || @event.ParticipationConfiguration?.ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.PlatformManaged
            || !await events.IsPubliclyEligibleAsync(@event.TenantId, @event.Id, cancellationToken))
        {
            return null;
        }

        var catalog = await catalogs.GetPublishedCatalogAsync(@event.Id, @event.TenantId, cancellationToken);
        if (catalog is null)
        {
            return null;
        }

        var ticketTypes = catalog.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .ToArray();
        var feePolicy = await feePolicies.GetActiveAsync(cancellationToken);
        TenantDirectoryOperatorPublicDto? directoryOperator = null;
        if (ticketTypes.Any(ticketType => ticketType.TicketPricingModeId != (int)TicketPricingModeEnum.Free))
        {
            TenantDirectoryOperatorReadinessAssessment readiness =
                await directoryOperatorReadiness.EvaluateAsync(
                    @event.TenantId,
                    TenantDirectoryOperatorIdentityCapability.PaidCommerce,
                    cancellationToken);
            if (!readiness.IsReady || readiness.Identity is not { } identity ||
                readiness.DocumentRevision is not { } revision)
            {
                return null;
            }

            directoryOperator = new TenantDirectoryOperatorPublicDto
            {
                DocumentRevision = revision,
                PublicName = identity.PublicName,
                LegalName = identity.LegalName,
                OperatorKindCode = identity.OperatorKindCode,
                JurisdictionCountryCode = identity.JurisdictionCountryCode,
                RegistrationIdentifier = identity.RegistrationIdentifier,
                PublicContactEmail = identity.PublicContactEmail,
                LegalNoticeUrl = identity.LegalNoticeUrl,
                TermsUrl = identity.TermsUrl,
                PrivacyUrl = identity.PrivacyUrl
            };
        }

        return new RegistrationCheckoutCompositionDto
        {
            EventId = @event.Id,
            TicketCatalogVersionId = catalog.Id,
            CurrencyCode = catalog.CurrencyCode,
            DirectoryOperator = directoryOperator,
            TicketTypes = ticketTypes
                .Select(ticketType => new RegistrationCheckoutTicketTypeDto
                    {
                        Id = ticketType.Id,
                        Name = ticketType.Name,
                        TicketPricingModeId = ticketType.TicketPricingModeId,
                        TicketPricingModeCode = ticketType.TicketPricingMode?.MasterCode,
                        FixedPriceMinor = ticketType.FixedPriceMinor,
                        MinimumPriceMinor = ticketType.MinimumPriceMinor,
                        SuggestedPriceMinor = ticketType.SuggestedPriceMinor,
                        PerOrderLimit = ticketType.PerOrderLimit,
                        SlidingScaleOptions = ticketType.TicketPricingModeId == (int)TicketPricingModeEnum.SlidingScale
                            ? BuildSlidingScaleOptions(
                                catalog.CurrencyCode,
                                ticketType.MinimumPriceMinor.GetValueOrDefault(),
                                ticketType.SuggestedPriceMinor.GetValueOrDefault(ticketType.MinimumPriceMinor.GetValueOrDefault()),
                                feePolicy,
                                earningsCalculator)
                            : []
                    })
                .ToArray()
        };
    }

    private static IReadOnlyList<RegistrationCheckoutSlidingScaleOptionDto> BuildSlidingScaleOptions(
        string currencyCode,
        long minimumMinor,
        long suggestedMinor,
        Explore.Domain.PlatformFeePolicy? feePolicy,
        IOrganizerEarningsCalculator earningsCalculator)
    {
        var maximumMinor = Math.Max(minimumMinor, suggestedMinor);
        var pointCount = maximumMinor == minimumMinor ? 1 : 5;
        return Enumerable.Range(0, pointCount)
            .Select(index => pointCount == 1
                ? minimumMinor
                : minimumMinor + ((maximumMinor - minimumMinor) * index / (pointCount - 1)))
            .Distinct()
            .Select(priceMinor => new RegistrationCheckoutSlidingScaleOptionDto
            {
                BuyerPriceMinor = priceMinor,
                OrganizerEarningsMinor = earningsCalculator.Calculate(currencyCode, priceMinor, feePolicy).OrganizerEarningsMinor
            })
            .ToArray();
    }
}
