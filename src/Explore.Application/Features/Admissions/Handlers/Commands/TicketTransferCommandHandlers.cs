using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.Admissions.Handlers.Commands;

public sealed class OfferTicketTransferCommandHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService capabilityTokens,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IRequestHandler<
        OfferTicketTransferCommand,
        TicketTransferOfferDto?>
{
    public async Task<TicketTransferOfferDto?> Handle(
        OfferTicketTransferCommand request,
        CancellationToken cancellationToken)
    {
        Guid? userId = currentUser.UserId;
        if (!currentUser.IsAuthenticated
            || !userId.HasValue)
        {
            return null;
        }

        Guid tenantId = tenantContext.TenantId;
        AdmissionTicket? ticket =
            await repository.GetTicketAsync(
                tenantId,
                request.EventId,
                request.AdmissionTicketId,
                cancellationToken);
        if (ticket is null)
        {
            return null;
        }
        RegistrationOrder? order =
            await repository.GetOrderAsync(
                tenantId,
                request.EventId,
                ticket.RegistrationOrderId,
                cancellationToken);
        DateTime? eventStartsAt =
            await repository.GetEventStartsAtUtcAsync(
                tenantId,
                request.EventId,
                cancellationToken);
        if (order is null
            || !eventStartsAt.HasValue
            || ticket.HolderSubjectUserId != userId
            && order.AccountUserId != userId)
        {
            return null;
        }

        GuestCapabilityTokenIssue issued =
            capabilityTokens.Issue();
        DateTime offeredAt =
            timeProvider.GetUtcNow().UtcDateTime;
        AdmissionTicketTransferResult result =
            await unitOfWork.ExecuteInTransactionAsync(
                token => repository.OfferAsync(
                    new AdmissionTicketTransferOfferRequest(
                        tenantId,
                        request.EventId,
                        request.AdmissionTicketId,
                        Guid.CreateVersion7(),
                        issued.Hash.Value,
                        eventStartsAt.Value,
                        offeredAt,
                        userId),
                    token),
                cancellationToken);
        return result.Outcome !=
                AdmissionTicketTransferOutcome.Offered
            || result.Transfer is null
            || result.Ticket is null
                ? null
                : new TicketTransferOfferDto
                {
                    Transfer = TicketTransferMapping.ToDto(
                        result.Transfer,
                        result.Ticket,
                        canCancel: true),
                    ClaimCapability =
                        issued.RawToken,
                };
    }
}

public sealed class AcceptTicketTransferCommandHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService capabilityTokens,
    IAdmissionCredentialDigestService credentials,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IRequestHandler<
        AcceptTicketTransferCommand,
        TicketTransferAcceptanceDto?>
{
    public async Task<TicketTransferAcceptanceDto?> Handle(
        AcceptTicketTransferCommand request,
        CancellationToken cancellationToken)
    {
        Guid? userId = currentUser.UserId;
        if (!currentUser.IsAuthenticated
            || !userId.HasValue
            || request.RecipientParticipantId == Guid.Empty)
        {
            return null;
        }

        Guid tenantId = tenantContext.TenantId;
        AdmissionTicketTransferAccessContext? access =
            await repository.GetAccessAsync(
                tenantId,
                request.EventId,
                request.AdmissionTicketId,
                request.AdmissionTicketTransferId,
                cancellationToken);
        if (access is null
            || !capabilityTokens.Matches(
                request.CapabilityToken,
                CapabilityTokenHash.Create(
                    access.Transfer.CapabilityDigest)))
        {
            return null;
        }

        Guid credentialId = Guid.CreateVersion7();
        AdmissionCredentialMaterial material =
            await credentials.CreateAsync(
                new AdmissionCredentialCreateRequest(
                    tenantId,
                    request.AdmissionTicketId,
                    credentialId,
                    "AdmissionTicket",
                    access.Ticket.CredentialGeneration + 1),
                cancellationToken);
        DateTime acceptedAt =
            timeProvider.GetUtcNow().UtcDateTime;
        AdmissionTicketTransferResult result =
            await unitOfWork.ExecuteInTransactionAsync(
                token =>
                    repository.ApplyAcceptanceAsync(
                        new AdmissionTicketTransferAcceptanceRequest(
                            tenantId,
                            request.EventId,
                            request.AdmissionTicketId,
                            request.AdmissionTicketTransferId,
                            access.Transfer.CapabilityDigest,
                            access.Ticket.CredentialGeneration,
                            request.RecipientParticipantId,
                            userId.Value,
                            RequirementsComplete: true,
                            SubjectConsentRecordId: null,
                            ApprovedByActorId: null,
                            credentialId,
                            material.KeyVersion,
                            material.LookupDigest,
                            Guid.CreateVersion7(),
                            Guid.CreateVersion7(),
                            acceptedAt,
                            userId),
                        token),
                cancellationToken);
        return result.Outcome !=
                AdmissionTicketTransferOutcome.Accepted
            || result.Transfer is null
            || result.Ticket is null
                ? null
                : new TicketTransferAcceptanceDto
                {
                    Transfer = TicketTransferMapping.ToDto(
                        result.Transfer,
                        result.Ticket,
                        canOffer: true,
                        canCorrect: true,
                        canReissue: true),
                    Credential =
                        material.PlaintextCredential,
                };
    }
}

public sealed class CancelTicketTransferCommandHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IRequestHandler<
        CancelTicketTransferCommand,
        TicketTransferDto?>
{
    public async Task<TicketTransferDto?> Handle(
        CancelTicketTransferCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not { } userId)
        {
            return null;
        }

        AdmissionTicketTransferResult result =
            await unitOfWork.ExecuteInTransactionAsync(
                token => repository.CancelAsync(
                    tenantContext.TenantId,
                    request.EventId,
                    request.AdmissionTicketId,
                    request.AdmissionTicketTransferId,
                    userId,
                    timeProvider.GetUtcNow().UtcDateTime,
                    token),
                cancellationToken);
        return result.Transfer is null
            || result.Ticket is null
                ? null
                : TicketTransferMapping.ToDto(
                    result.Transfer,
                    result.Ticket);
    }
}

public abstract class RotateTransferredTicketHandler
{
    private readonly IAdmissionTicketTransferRepository repository;
    private readonly ITenantContext tenantContext;
    private readonly ICurrentUserService currentUser;
    private readonly IAdmissionCredentialDigestService credentials;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    protected RotateTransferredTicketHandler(
        IAdmissionTicketTransferRepository repository,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        IAdmissionCredentialDigestService credentials,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.repository = repository;
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.credentials = credentials;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    protected async Task<TicketTransferAcceptanceDto?>
        RotateAsync(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            string eventType,
            CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not { } userId)
        {
            return null;
        }
        AdmissionTicketTransferAccessContext? access =
            await repository.GetAccessAsync(
                tenantContext.TenantId,
                eventId,
                admissionTicketId,
                transferId,
                cancellationToken);
        if (access is null
            || access.Ticket.HolderSubjectUserId != userId)
        {
            return null;
        }

        Guid credentialId = Guid.CreateVersion7();
        AdmissionCredentialMaterial material =
            await credentials.CreateAsync(
                new AdmissionCredentialCreateRequest(
                    tenantContext.TenantId,
                    admissionTicketId,
                    credentialId,
                    "AdmissionTicket",
                    access.Ticket.CredentialGeneration + 1),
                cancellationToken);
        AdmissionTicketTransferResult result =
            await unitOfWork.ExecuteInTransactionAsync(
                token => repository.RotateForHolderAsync(
                    tenantContext.TenantId,
                    eventId,
                    admissionTicketId,
                    transferId,
                    userId,
                    credentialId,
                    material.KeyVersion,
                    material.LookupDigest,
                    Guid.CreateVersion7(),
                    eventType,
                    timeProvider.GetUtcNow().UtcDateTime,
                    token),
                cancellationToken);
        return result.Transfer is null
            || result.Ticket is null
                ? null
                : new TicketTransferAcceptanceDto
                {
                    Transfer = TicketTransferMapping.ToDto(
                        result.Transfer,
                        result.Ticket,
                        canOffer: true,
                        canCorrect: true,
                        canReissue: true),
                    Credential =
                        material.PlaintextCredential,
                };
    }
}

public sealed class CorrectTicketTransferCommandHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAdmissionCredentialDigestService credentials,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    RotateTransferredTicketHandler(
        repository,
        tenantContext,
        currentUser,
        credentials,
        unitOfWork,
        timeProvider),
    IRequestHandler<
        CorrectTicketTransferCommand,
        TicketTransferAcceptanceDto?>
{
    public Task<TicketTransferAcceptanceDto?> Handle(
        CorrectTicketTransferCommand request,
        CancellationToken cancellationToken) =>
        RotateAsync(
            request.EventId,
            request.AdmissionTicketId,
            request.AdmissionTicketTransferId,
            "AdmissionTicketTransferCorrected",
            cancellationToken);
}

public sealed class ReissueTransferredTicketCommandHandler(
    IAdmissionTicketTransferRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAdmissionCredentialDigestService credentials,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    RotateTransferredTicketHandler(
        repository,
        tenantContext,
        currentUser,
        credentials,
        unitOfWork,
        timeProvider),
    IRequestHandler<
        ReissueTransferredTicketCommand,
        TicketTransferAcceptanceDto?>
{
    public Task<TicketTransferAcceptanceDto?> Handle(
        ReissueTransferredTicketCommand request,
        CancellationToken cancellationToken) =>
        RotateAsync(
            request.EventId,
            request.AdmissionTicketId,
            request.AdmissionTicketTransferId,
            "AdmissionTicketTransferReissued",
            cancellationToken);
}

internal static class TicketTransferMapping
{
    public static TicketTransferDto ToDto(
        AdmissionTicketTransfer transfer,
        AdmissionTicket ticket,
        bool canOffer = false,
        bool canAccept = false,
        bool canCancel = false,
        bool canCorrect = false,
        bool canReissue = false) =>
        new()
        {
            Id = transfer.Id,
            AdmissionTicketId =
                transfer.AdmissionTicketId,
            StatusCode = ((AdmissionTicketTransferStatus)
                transfer.StatusId)
                .ToString()
                .ToUpperInvariant(),
            SupportCode = transfer.StatusId switch
            {
                (int)AdmissionTicketTransferStatus.Offered =>
                    "recipient_action_required",
                (int)AdmissionTicketTransferStatus.Accepted =>
                    "none",
                _ => "contact_sender",
            },
            TransferHop = transfer.TransferHop,
            ExpiresAt = transfer.ExpiresAt,
            CredentialGeneration =
                ticket.CredentialGeneration,
            CanOffer = canOffer,
            CanAccept = canAccept,
            CanCancel = canCancel,
            CanCorrect = canCorrect,
            CanReissue = canReissue,
            EventId = transfer.EventId,
        };
}
