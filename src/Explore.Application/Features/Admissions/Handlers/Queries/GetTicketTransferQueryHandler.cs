using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Handlers.Commands;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.Admissions.Handlers.Queries;

public sealed class GetTicketTransferQueryHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService capabilityTokens,
    TimeProvider timeProvider) :
    IRequestHandler<GetTicketTransferQuery, TicketTransferDto?>
{
    public async Task<TicketTransferDto?> Handle(
        GetTicketTransferQuery request,
        CancellationToken cancellationToken)
    {
        AdmissionTicketTransferAccessContext? access =
            await repository.GetAccessAsync(
                tenantContext.TenantId,
                request.EventId,
                request.AdmissionTicketId,
                request.AdmissionTicketTransferId,
                cancellationToken);
        if (access is null)
        {
            return null;
        }

        bool capabilityValid =
            access.Transfer.IsOpen
            && timeProvider.GetUtcNow().UtcDateTime <= access.Transfer.ExpiresAt
            && capabilityTokens.Matches(
                request.CapabilityToken,
                CapabilityTokenHash.Create(access.Transfer.CapabilityDigest));
        Guid? userId = currentUser.UserId;
        bool sourceAuthority = userId.HasValue
            && (access.SourceParticipant.LinkedUserId == userId
                || access.Order.AccountUserId == userId);
        bool holderAuthority = userId.HasValue
            && access.Ticket.HolderSubjectUserId == userId;
        bool recipientAuthority = userId.HasValue
            && access.RecipientParticipant?.LinkedUserId == userId;
        if (!capabilityValid
            && !sourceAuthority
            && !holderAuthority
            && !recipientAuthority)
        {
            return null;
        }

        return TicketTransferMapping.ToDto(
            access.Transfer,
            access.Ticket,
            canOffer: holderAuthority
                && access.Transfer.StatusId ==
                (int)AdmissionTicketTransferStatus.Accepted,
            canAccept: capabilityValid
                && currentUser.IsAuthenticated
                && access.Transfer.IsOpen,
            canCancel: sourceAuthority && access.Transfer.IsOpen,
            canCorrect: holderAuthority
                && access.Transfer.StatusId ==
                (int)AdmissionTicketTransferStatus.Accepted,
            canReissue: holderAuthority
                && access.Transfer.StatusId ==
                (int)AdmissionTicketTransferStatus.Accepted);
    }
}
