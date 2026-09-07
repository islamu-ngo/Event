using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Features.Waitlist.Requests.Commands;
using Explore.Application.Services.Registration;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Waitlist.Handlers.Commands;

public sealed class JoinFairReturnWaitlistCommandHandler(
    IFairReturnWaitlistRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IPaidCheckoutActivationService activation,
    TimeProvider timeProvider) :
    IRequestHandler<
        JoinFairReturnWaitlistCommand,
        FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> Handle(
        JoinFairReturnWaitlistCommand request,
        CancellationToken cancellationToken)
    {
        Guid? userId = currentUser.UserId;
        FairReturnWaitlistAccessContext? access =
            await repository.GetAccessAsync(
                tenantContext.TenantId,
                request.EventId,
                request.RegistrationOrderId,
                request.RegistrationOrderLineId,
                cancellationToken);
        if (!currentUser.IsAuthenticated
            || !userId.HasValue
            || access is null
            || access.Order.AccountUserId != userId
            || access.Entry is not null
            || access.Supply is not null
            || access.Policy is not
                { IsEnabled: true }
            || access.PurchaseOperation is null
            || access.Line.Quantity != 1
            || !((await activation
                    .EvaluateSaleControlAsync(
                        access.Order.TenantId,
                        access.Order.EventId,
                        cancellationToken))
                .IsActive))
        {
            return null;
        }
        Guid? participantId = access.Line
            .Assignments
            .Select(value => value.ParticipantId)
            .SingleOrDefault(value =>
                value.HasValue);
        if (!participantId.HasValue)
        {
            return null;
        }
        DateTime now =
            timeProvider.GetUtcNow().UtcDateTime;
        EventWaitlistEntry entry =
            EventWaitlistEntry.Enqueue(
                Guid.CreateVersion7(),
                access.Order.TenantId,
                access.Order.EventId,
                access.Line.TicketTypeId,
                access.Line.TicketCatalogVersionId,
                access.PurchaseOperation
                    .PolicyVersionId,
                access.Order.Id,
                access.Line.Id,
                participantId.Value,
                userId.Value,
                access.Line.CurrencyCodeSnapshot,
                Digest(
                    $"commercial|" +
                    $"{access.PurchaseOperation.PolicyVersionId:N}|" +
                    $"{access.Line.UnitPriceAmountSnapshot}|" +
                    $"{access.Line.PostDiscountLineSubtotalMinorSnapshot}|" +
                    $"{access.Line.CurrencyCodeSnapshot}"),
                Digest(
                    $"admission|" +
                    $"{access.Line.TicketCatalogVersionId:N}|" +
                    $"{access.Line.TicketTypeId:N}"),
                access.Line
                    .PostDiscountLineSubtotalMinorSnapshot,
                refundFundingModeId: 1,
                priority: 0,
                now);
        entry = await repository.EnqueueAsync(
            entry,
            cancellationToken);
        _ = await repository.AllocateAsync(
            new FairReturnAllocationRequest(
                entry.TenantId,
                entry.EventId,
                access.Policy.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                now),
            cancellationToken);
        FairReturnWaitlistAccessContext updated =
            (await repository.GetAccessAsync(
                entry.TenantId,
                entry.EventId,
                entry.RegistrationOrderId,
                entry.RegistrationOrderLineId,
                cancellationToken))!;
        return FairReturnWaitlistMapping.ToDto(
            updated,
            controlsOpen: true,
            userId,
            replacementSettled: false);
    }

    private static string Digest(string value) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));
}

public sealed class LeaveFairReturnWaitlistCommandHandler(
    IFairReturnWaitlistRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IPaidCheckoutActivationService activation,
    TimeProvider timeProvider) :
    IRequestHandler<
        LeaveFairReturnWaitlistCommand,
        FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> Handle(
        LeaveFairReturnWaitlistCommand request,
        CancellationToken cancellationToken)
    {
        FairReturnWaitlistAccessContext? access =
            await FairReturnWaitlistMapping
                .OwnedOpenAccessAsync(
                    repository,
                    activation,
                    tenantContext.TenantId,
                    request.EventId,
                    request.RegistrationOrderId,
                    request.RegistrationOrderLineId,
                    currentUser,
                    cancellationToken);
        if (access?.Entry is null)
        {
            return null;
        }
        EventWaitlistEntry? entry =
            await repository.LeaveAsync(
                access.Order.TenantId,
                access.Order.EventId,
                access.Line.Id,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        return entry is null
            ? null
            : FairReturnWaitlistMapping.ToDto(
                access with
                {
                    Entry = entry,
                    Offer = null,
                },
                controlsOpen: true,
                currentUser.UserId,
                replacementSettled: false);
    }
}

public sealed class AcceptFairReturnOfferCommandHandler(
    IFairReturnWaitlistRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IPaidCheckoutActivationService activation,
    TimeProvider timeProvider) :
    IRequestHandler<
        AcceptFairReturnOfferCommand,
        FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> Handle(
        AcceptFairReturnOfferCommand request,
        CancellationToken cancellationToken)
    {
        FairReturnWaitlistAccessContext? access =
            await FairReturnWaitlistMapping
                .OwnedOpenAccessAsync(
                    repository,
                    activation,
                    tenantContext.TenantId,
                    request.EventId,
                    request.RegistrationOrderId,
                    request.RegistrationOrderLineId,
                    currentUser,
                    cancellationToken);
        if (access?.Offer?.Id != request.OfferId
            || access.Binding is null
            || !await repository
                .HasReplacementSettlementAsync(
                    access.Order.TenantId,
                    access.Binding.Id,
                    cancellationToken))
        {
            return null;
        }
        FairReturnWaitlistResult result =
            await repository
                .FinalizeReplacementAsync(
                    new
                        WaitlistReplacementFinalizeRequest(
                            access.Order.TenantId,
                            access.Order.EventId,
                            request.OfferId,
                            timeProvider.GetUtcNow()
                                .UtcDateTime),
                    cancellationToken);
        return result.Entry is null
            ? null
            : FairReturnWaitlistMapping.ToDto(
                access with
                {
                    Entry = result.Entry,
                    Offer = result.Offer,
                    Binding = result.Binding,
                },
                controlsOpen: true,
                currentUser.UserId,
                replacementSettled: true);
    }
}

public sealed class WithdrawFairReturnSupplyCommandHandler(
    IFairReturnWaitlistRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IPaidCheckoutActivationService activation,
    TimeProvider timeProvider) :
    IRequestHandler<
        WithdrawFairReturnSupplyCommand,
        FairReturnWaitlistDto?>
{
    public async Task<FairReturnWaitlistDto?> Handle(
        WithdrawFairReturnSupplyCommand request,
        CancellationToken cancellationToken)
    {
        FairReturnWaitlistAccessContext? access =
            await FairReturnWaitlistMapping
                .OwnedOpenAccessAsync(
                    repository,
                    activation,
                    tenantContext.TenantId,
                    request.EventId,
                    request.RegistrationOrderId,
                    request.RegistrationOrderLineId,
                    currentUser,
                    cancellationToken);
        if (access?.Supply?.Id != request.SupplyId)
        {
            return null;
        }
        FairReturnWaitlistResult result =
            await repository.WithdrawAsync(
                new FairReturnWithdrawalRequest(
                    access.Order.TenantId,
                    access.Order.EventId,
                    request.SupplyId,
                    timeProvider.GetUtcNow()
                        .UtcDateTime),
                cancellationToken);
        return result.Supply is null
            ? null
            : FairReturnWaitlistMapping.ToDto(
                access with
                {
                    Supply = result.Supply,
                    Binding = result.Binding,
                },
                controlsOpen: true,
                currentUser.UserId,
                replacementSettled: false);
    }
}

internal static class FairReturnWaitlistMapping
{
    public static bool HasReadAuthority(
        FairReturnWaitlistAccessContext access,
        Guid? userId,
        string? capabilityToken,
        IGuestCapabilityTokenService tokens) =>
        userId.HasValue
        && access.Order.AccountUserId == userId
        || access.Order.GuestAccessTokenHash is
            { } hash
        && tokens.Matches(
            capabilityToken,
            hash);

    public static async Task<
        FairReturnWaitlistAccessContext?>
        OwnedOpenAccessAsync(
            IFairReturnWaitlistRepository repository,
            IPaidCheckoutActivationService activation,
            Guid tenantId,
            Guid eventId,
            Guid orderId,
            Guid lineId,
            ICurrentUserService currentUser,
            CancellationToken cancellationToken)
    {
        FairReturnWaitlistAccessContext? access =
            await repository.GetAccessAsync(
                tenantId,
                eventId,
                orderId,
                lineId,
                cancellationToken);
        return access is not null
            && currentUser.IsAuthenticated
            && currentUser.UserId.HasValue
            && access.Order.AccountUserId ==
                currentUser.UserId
            && (await activation
                .EvaluateSaleControlAsync(
                    tenantId,
                    eventId,
                    cancellationToken)).IsActive
                ? access
                : null;
    }

    public static FairReturnWaitlistDto ToDto(
        FairReturnWaitlistAccessContext access,
        bool controlsOpen,
        Guid? userId,
        bool replacementSettled)
    {
        bool owner = userId.HasValue
            && access.Order.AccountUserId ==
            userId;
        EventWaitlistEntryStatus? entryStatus =
            access.Entry is null
                ? null
                : (EventWaitlistEntryStatus)
                    access.Entry.StatusId;
        FairReturnSupplyStatus? supplyStatus =
            access.Supply is null
                ? null
                : (FairReturnSupplyStatus)
                    access.Supply.StatusId;
        string statusCode = entryStatus?.ToString()
            .ToUpperInvariant()
            ?? supplyStatus?.ToString()
                .ToUpperInvariant()
            ?? "AVAILABLE";
        string reasonCode = entryStatus switch
        {
            EventWaitlistEntryStatus.Queued =>
                "AWAITING_SUPPLY",
            EventWaitlistEntryStatus.Offered =>
                replacementSettled
                    ? "REPLACEMENT_SETTLED"
                    : "PAYMENT_PENDING",
            EventWaitlistEntryStatus.Converted =>
                "COMPLETED",
            EventWaitlistEntryStatus.Withdrawn =>
                "WITHDRAWN",
            _ => supplyStatus switch
            {
                FairReturnSupplyStatus.Bound =>
                    "SELLER_CONFLICT",
                FairReturnSupplyStatus.Withdrawn =>
                    "WITHDRAWN",
                FairReturnSupplyStatus.Available =>
                    "SUPPLY_AVAILABLE",
                _ => "WAITLIST_AVAILABLE",
            },
        };
        return new FairReturnWaitlistDto
        {
            Id = access.Entry?.Id
                ?? access.Supply?.Id
                ?? access.Line.Id,
            StatusCode = statusCode,
            Position = access.Position <= 0
                ? FairReturnWaitlistDto
                    .PositionUnavailable
                : (int)Math.Min(
                    access.Position,
                    FairReturnWaitlistDto
                        .MaximumPublishedPosition),
            ReasonCode = reasonCode,
            OfferExpiresAt =
                access.Offer?.ExpiresAt,
            CanJoin = owner
                && controlsOpen
                && access.Policy?.IsEnabled == true
                && access.Entry is null
                && access.Supply is null,
            CanLeave = owner
                && controlsOpen
                && entryStatus ==
                    EventWaitlistEntryStatus.Queued,
            CanAcceptOffer = owner
                && controlsOpen
                && entryStatus ==
                    EventWaitlistEntryStatus.Offered
                && replacementSettled,
            CanWithdrawSupply = owner
                && controlsOpen
                && supplyStatus is
                    FairReturnSupplyStatus.Available
                    or FairReturnSupplyStatus.Bound,
            AllocationOpen = controlsOpen,
            WithdrawalOpen = controlsOpen,
            EventId = access.Order.EventId,
            RegistrationOrderId =
                access.Order.Id,
            RegistrationOrderLineId =
                access.Line.Id,
            OfferId = access.Offer?.Id,
            SupplyId = access.Supply?.Id,
        };
    }
}
