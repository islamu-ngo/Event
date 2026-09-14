using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Handlers.Commands;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Admissions.Handlers.Queries;

public sealed class GetTicketTransferQueryHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService capabilityTokens,
    TimeProvider timeProvider) :
    IQueryHandler<GetTicketTransferQuery, TicketTransferDto?>
{
    public async Task<TicketTransferDto?> QueryAsync(
        GetTicketTransferQuery query,
        CancellationToken cancellationToken = default)
    {
        AdmissionTicketTransferAccessContext? access =
            await repository.GetAccessAsync(
                tenantContext.TenantId,
                query.EventId,
                query.AdmissionTicketId,
                query.AdmissionTicketTransferId,
                cancellationToken);
        if (access is null)
        {
            return null;
        }

        bool capabilityValid =
            access.Transfer.IsOpen
            && timeProvider.GetUtcNow().UtcDateTime <= access.Transfer.ExpiresAt
            && capabilityTokens.Matches(
                query.CapabilityToken,
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
