using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionTicketTransferRepository(
    ExploreDbContext dbContext) :
    IAdmissionTicketTransferRepository
{
    public const string CanonicalFenceOrder =
        "assignment>eligibility>ticket>transfer";

    public Task<AdmissionTicket?> GetTicketAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken) =>
        dbContext.AdmissionTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == admissionTicketId,
                cancellationToken);

    public Task<RegistrationOrder?> GetOrderAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == registrationOrderId,
                cancellationToken);

    public async Task<DateTime?> GetEventStartsAtUtcAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        DateOnly? date = await dbContext.Events
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId
                && value.Id == eventId)
            .Select(value =>
                (DateOnly?)value.FirstSessionDate)
            .SingleOrDefaultAsync(cancellationToken);
        return date?.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);
    }

    public async Task<AdmissionTicketTransferAccessContext?>
        GetAccessAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            Guid admissionTicketTransferId,
            CancellationToken cancellationToken)
    {
        AdmissionTicketTransfer? transfer =
            await dbContext.AdmissionTicketTransfers
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.AdmissionTicketId ==
                    admissionTicketId
                    && value.Id ==
                    admissionTicketTransferId,
                    cancellationToken);
        if (transfer is null)
        {
            return null;
        }

        AdmissionTicket? ticket =
            await dbContext.AdmissionTickets
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == admissionTicketId,
                    cancellationToken);
        RegistrationOrder? order =
            await dbContext.RegistrationOrders
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id ==
                    transfer.RegistrationOrderId,
                    cancellationToken);
        RegistrationParticipant? source =
            await dbContext.RegistrationParticipants
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.RegistrationOrderId ==
                    transfer.RegistrationOrderId
                    && value.Id ==
                    transfer.FromParticipantId,
                    cancellationToken);
        RegistrationParticipant? recipient =
            transfer.ToParticipantId.HasValue
                ? await dbContext.RegistrationParticipants
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == tenantId
                        && value.RegistrationOrderId ==
                        transfer.RegistrationOrderId
                        && value.Id ==
                        transfer.ToParticipantId.Value,
                        cancellationToken)
                : null;
        return ticket is null
            || order is null
            || source is null
                ? null
                : new AdmissionTicketTransferAccessContext(
                    transfer,
                    ticket,
                    order,
                    source,
                    recipient);
    }

    public Task<AdmissionTicketTransferContext?>
        LoadForOfferAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            CancellationToken cancellationToken) =>
        LoadCanonicalAsync(
            tenantId,
            eventId,
            admissionTicketId,
            transferId: null,
            cancellationToken);

    public Task<AdmissionTicketTransferContext?>
        LoadForAcceptanceAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            Guid admissionTicketTransferId,
            CancellationToken cancellationToken) =>
        LoadCanonicalAsync(
            tenantId,
            eventId,
            admissionTicketId,
            admissionTicketTransferId,
            cancellationToken);

    public Task<AdmissionTicketTransferContext?>
        LoadForCorrectionAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            CancellationToken cancellationToken) =>
        LoadCanonicalAsync(
            tenantId,
            eventId,
            admissionTicketId,
            transferId: null,
            cancellationToken);

    public Task<AdmissionTicketTransferContext?>
        LoadForReissueAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            CancellationToken cancellationToken) =>
        LoadCanonicalAsync(
            tenantId,
            eventId,
            admissionTicketId,
            transferId: null,
            cancellationToken);

    public async Task<AdmissionTicketTransfer?>
        ResolveCapabilityForUpdateAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            string capabilityDigest,
            CancellationToken cancellationToken)
    {
        RequireTransaction();
        AdmissionTicketTransfer? candidate =
            await dbContext.AdmissionTicketTransfers
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.AdmissionTicketId ==
                    admissionTicketId
                    && value.CapabilityDigest ==
                    capabilityDigest,
                    cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        AdmissionTicketTransferContext? context =
            await LoadForAcceptanceAsync(
                tenantId,
                eventId,
                admissionTicketId,
                candidate.Id,
                cancellationToken);
        return context?.Transfer is { } transfer
            && transfer.MatchesCapability(capabilityDigest)
                ? transfer
                : null;
    }

    public async Task<AdmissionTicketTransferResult>
        OfferAsync(
            AdmissionTicketTransferOfferRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdmissionTicketTransferContext? context =
            await LoadForOfferAsync(
                request.TenantId,
                request.EventId,
                request.AdmissionTicketId,
                cancellationToken);
        if (context is null)
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }
        if (request.AuthorityUserId.HasValue
            && !await HasHolderAuthorityAsync(
                context.Ticket,
                request.AuthorityUserId.Value,
                cancellationToken))
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }
        if (context.AlreadyCheckedIn)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .AlreadyCheckedIn,
                ticket: context.Ticket);
        }

        AdmissionTicketTransfer? replay =
            await dbContext.AdmissionTicketTransfers
                .SingleOrDefaultAsync(value =>
                    value.TenantId == request.TenantId
                    && value.EventId == request.EventId
                    && value.AdmissionTicketId ==
                    request.AdmissionTicketId
                    && value.OfferOperationKey ==
                    request.OfferOperationKey,
                    cancellationToken);
        if (replay is not null)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .AlreadyOffered,
                replay,
                context.Ticket);
        }

        AdmissionTicketTransfer? open = context.Transfer;
        if (open is not null)
        {
            if (request.OfferedAtUtc < open.ExpiresAt)
            {
                return Result(
                    AdmissionTicketTransferOutcome
                        .AlreadyOffered,
                    open,
                    context.Ticket);
            }

            open.Expire(request.OfferedAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }

        if (!context.Policy.IsEnabled)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .NotTransferable,
                ticket: context.Ticket);
        }
        if (context.Ticket.TransferHopCount >=
            context.Policy.MaximumHops)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .HopLimitReached,
                ticket: context.Ticket);
        }
        if (!context.Policy.GetOfferExpiry(
                context.Ticket.TransferHopCount,
                request.EventStartsAtUtc,
                request.OfferedAtUtc)
            .HasValue)
        {
            return Result(
                AdmissionTicketTransferOutcome.Expired,
                ticket: context.Ticket);
        }

        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                context.Ticket,
                context.Policy,
                request.OfferOperationKey,
                request.CapabilityDigest,
                request.EventStartsAtUtc,
                request.OfferedAtUtc);
        await dbContext.AdmissionTicketTransfers
            .AddAsync(transfer, cancellationToken);
        await dbContext.SaveChangesAsync(
            cancellationToken);
        return Result(
            AdmissionTicketTransferOutcome.Offered,
            transfer,
            context.Ticket);
    }

    public async Task<AdmissionTicketTransferResult>
        ApplyAcceptanceAsync(
            AdmissionTicketTransferAcceptanceRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdmissionRecoveryCapability[] recoveryCapabilities =
            await LoadRecoveryCapabilitiesForUpdateAsync(
                request.TenantId,
                request.AdmissionTicketId,
                cancellationToken);
        AdmissionTicketTransferContext? context =
            await LoadForAcceptanceAsync(
                request.TenantId,
                request.EventId,
                request.AdmissionTicketId,
                request.AdmissionTicketTransferId,
                cancellationToken);
        if (context?.Transfer is not { } transfer
            || !transfer.MatchesCapability(
                request.CapabilityDigest))
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }
        if (request.AcceptedAtUtc > transfer.ExpiresAt)
        {
            transfer.Expire(request.AcceptedAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return Result(
                AdmissionTicketTransferOutcome.Expired,
                transfer,
                context.Ticket);
        }
        if (context.AlreadyCheckedIn)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .AlreadyCheckedIn,
                transfer,
                context.Ticket);
        }
        if (request.ExpectedCredentialGeneration !=
                context.Ticket.CredentialGeneration
            || transfer.CredentialGeneration !=
            context.Ticket.CredentialGeneration)
        {
            return Result(
                AdmissionTicketTransferOutcome
                    .StaleGeneration,
                transfer,
                context.Ticket);
        }

        RegistrationParticipant? recipient =
            await dbContext.RegistrationParticipants
                .SingleOrDefaultAsync(value =>
                    value.TenantId == request.TenantId
                    && value.RegistrationOrderId ==
                    context.Ticket.RegistrationOrderId
                    && value.Id ==
                    request.RecipientParticipantId
                    && value.LinkedUserId ==
                    request.RecipientSubjectUserId,
                    cancellationToken);
        if (recipient is null)
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }
        if (request.AuthorityUserId.HasValue
            && request.AuthorityUserId !=
            recipient.LinkedUserId)
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }

        var readinessRepository =
            new ParticipantAdmissionEligibilityRepository(
                dbContext);
        ParticipantAdmissionTransferReadiness? readiness =
            await readinessRepository
                .ResolveTransferRecipientReadinessAsync(
                    context.Eligibility,
                    recipient,
                    request.RecipientSubjectUserId,
                    request.SubjectConsentRecordId,
                    request.ApprovedByActorId,
                    cancellationToken);
        if (readiness is null
            || !readiness.IsReady(context.Eligibility))
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }

        context.Eligibility.TransferTo(
            recipient,
            request.RecipientSubjectUserId,
            readiness.SubjectConsentRecordId,
            readiness.RequirementsComplete,
            readiness.ApprovedByActorId,
            request.AcceptedAtUtc,
            Guid.CreateVersion7());
        context.Assignment.Assign(
            recipient,
            Guid.CreateVersion7());
        context.Ticket.AcceptTransfer(
            transfer,
            recipient,
            request.RecipientSubjectUserId,
            request.CredentialId,
            context.Ticket.CredentialGeneration + 1,
            request.LookupKeyVersion,
            request.LookupDigest,
            request.AcceptedAtUtc);
        foreach (
            AdmissionRecoveryCapability recoveryCapability
            in recoveryCapabilities)
        {
            recoveryCapability.TryRotate(
                request.AcceptedAtUtc);
        }

        var outbox = new OutboxMessage
        {
            Id = request.OutboxMessageId,
            AggregateType =
                nameof(AdmissionTicketTransfer),
            AggregateId = transfer.Id,
            EventType =
                "AdmissionTicketTransferAccepted",
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = request.AcceptedAtUtc,
            MaxRetries = 10,
        };
        AdmissionTransferDeliveryIntent intent =
            AdmissionTransferDeliveryIntent.Create(
                request.DeliveryIntentId,
                transfer,
                request.OutboxMessageId,
                request.AcceptedAtUtc);
        await dbContext.OutboxMessages.AddAsync(
            outbox,
            cancellationToken);
        await dbContext.AdmissionTransferDeliveryIntents
            .AddAsync(intent, cancellationToken);
        await dbContext.SaveChangesAsync(
            cancellationToken);
        return Result(
            AdmissionTicketTransferOutcome.Accepted,
            transfer,
            context.Ticket);
    }

    public async Task<AdmissionTicketTransferResult>
        CancelAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            Guid admissionTicketTransferId,
            Guid authorityUserId,
            DateTime cancelledAtUtc,
            CancellationToken cancellationToken)
    {
        AdmissionTicketTransferContext? context =
            await LoadForAcceptanceAsync(
                tenantId,
                eventId,
                admissionTicketId,
                admissionTicketTransferId,
                cancellationToken);
        if (context?.Transfer is not { } transfer
            || !await HasHolderAuthorityAsync(
                context.Ticket,
                authorityUserId,
                cancellationToken))
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }

        transfer.Cancel(cancelledAtUtc);
        await dbContext.SaveChangesAsync(
            cancellationToken);
        return Result(
            AdmissionTicketTransferOutcome.Cancelled,
            transfer,
            context.Ticket);
    }

    public async Task<AdmissionTicketTransferResult>
        RotateForHolderAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            Guid admissionTicketTransferId,
            Guid authorityUserId,
            Guid credentialId,
            int lookupKeyVersion,
            string lookupDigest,
            Guid outboxMessageId,
            string eventType,
            DateTime rotatedAtUtc,
            CancellationToken cancellationToken)
    {
        AdmissionRecoveryCapability[] recoveryCapabilities =
            await LoadRecoveryCapabilitiesForUpdateAsync(
                tenantId,
                admissionTicketId,
                cancellationToken);
        AdmissionTicketTransferContext? context =
            await LoadForAcceptanceAsync(
                tenantId,
                eventId,
                admissionTicketId,
                admissionTicketTransferId,
                cancellationToken);
        if (context?.Transfer is not { } transfer
            || transfer.StatusId !=
            (int)AdmissionTicketTransferStatus.Accepted
            || context.Ticket.HolderSubjectUserId !=
            authorityUserId)
        {
            return Result(
                AdmissionTicketTransferOutcome.Unavailable);
        }

        context.Ticket.RotateCredential(
            credentialId,
            context.Ticket.CredentialGeneration + 1,
            lookupKeyVersion,
            lookupDigest,
            rotatedAtUtc);
        foreach (
            AdmissionRecoveryCapability recoveryCapability
            in recoveryCapabilities)
        {
            recoveryCapability.TryRotate(rotatedAtUtc);
        }

        await dbContext.OutboxMessages.AddAsync(
            new OutboxMessage
            {
                Id = outboxMessageId,
                AggregateType =
                    nameof(AdmissionTicketTransfer),
                AggregateId = transfer.Id,
                EventType = eventType,
                Payload = null,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = rotatedAtUtc,
                MaxRetries = 10,
            },
            cancellationToken);
        await dbContext.SaveChangesAsync(
            cancellationToken);
        return Result(
            AdmissionTicketTransferOutcome.Accepted,
            transfer,
            context.Ticket);
    }

    private async Task<AdmissionTicketTransferContext?>
        LoadCanonicalAsync(
            Guid tenantId,
            Guid eventId,
            Guid admissionTicketId,
            Guid? transferId,
            CancellationToken cancellationToken)
    {
        RequireTransaction();
        var identity = await dbContext.AdmissionTickets
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == admissionTicketId)
            .Select(value => new
            {
                value.RegistrationTicketAssignmentId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
        {
            return null;
        }

        await RelationalEntityRowFence
            .AcquireAsync<RegistrationTicketAssignment>(
                dbContext,
                tenantId,
                assignment => assignment.Id,
                identity.RegistrationTicketAssignmentId,
                cancellationToken);
        await RelationalEntityRowFence
            .AcquireAsync<ParticipantAdmissionEligibility>(
                dbContext,
                tenantId,
                eligibility => eligibility.RegistrationTicketAssignmentId,
                identity.RegistrationTicketAssignmentId,
                cancellationToken);
        await RelationalEntityRowFence
            .AcquireAsync<AdmissionTicket>(
                dbContext,
                tenantId,
                ticket => ticket.Id,
                admissionTicketId,
                cancellationToken);

        RegistrationTicketAssignment? assignment =
            await dbContext.RegistrationTicketAssignments
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.Id ==
                    identity.RegistrationTicketAssignmentId,
                    cancellationToken);
        ParticipantAdmissionEligibility? eligibility =
            await dbContext.ParticipantAdmissionEligibilities
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.RegistrationTicketAssignmentId ==
                    identity.RegistrationTicketAssignmentId,
                    cancellationToken);
        AdmissionTicket? ticket =
            await dbContext.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == admissionTicketId,
                    cancellationToken);
        if (assignment is null
            || eligibility is null
            || ticket is null)
        {
            return null;
        }

        TicketTransferPolicy? policy =
            await dbContext.TicketTransferPolicies
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.TicketCatalogVersionId ==
                    ticket.TicketCatalogVersionId
                    && value.EventTicketTypeId ==
                    ticket.EventTicketTypeId,
                    cancellationToken);
        if (policy is null)
        {
            return null;
        }

        AdmissionTicketTransfer? transfer = transferId.HasValue
            ? await dbContext.AdmissionTicketTransfers
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.AdmissionTicketId ==
                    admissionTicketId
                    && value.Id == transferId.Value,
                    cancellationToken)
            : await dbContext.AdmissionTicketTransfers
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.AdmissionTicketId ==
                    admissionTicketId
                    && value.StatusId ==
                    (int)AdmissionTicketTransferStatus
                        .Offered,
                    cancellationToken);
        if (transfer is not null)
        {
            await RelationalEntityRowFence
                .AcquireAsync<AdmissionTicketTransfer>(
                    dbContext,
                    tenantId,
                    candidate => candidate.Id,
                    transfer.Id,
                    cancellationToken);
            await dbContext.Entry(transfer)
                .ReloadAsync(cancellationToken);
        }

        bool alreadyCheckedIn =
            await dbContext.AdmissionCheckInEvents
                .AsNoTracking()
                .AnyAsync(value =>
                    value.TenantId == tenantId
                    && value.AdmissionTicketId ==
                    admissionTicketId,
                    cancellationToken);
        return new AdmissionTicketTransferContext(
            assignment,
            eligibility,
            ticket,
            policy,
            transfer,
            alreadyCheckedIn);
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.IsRelational()
            && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Ticket transfer persistence requires an active unit-of-work transaction.");
        }
    }

    private async Task<bool> HasHolderAuthorityAsync(
        AdmissionTicket ticket,
        Guid authorityUserId,
        CancellationToken cancellationToken)
    {
        if (authorityUserId == Guid.Empty)
        {
            return false;
        }
        if (ticket.HolderSubjectUserId ==
            authorityUserId)
        {
            return true;
        }

        return await dbContext.RegistrationOrders
            .AsNoTracking()
            .AnyAsync(value =>
                value.TenantId == ticket.TenantId
                && value.EventId == ticket.EventId
                && value.Id == ticket.RegistrationOrderId
                && value.AccountUserId ==
                authorityUserId,
                cancellationToken);
    }

    private async Task<AdmissionRecoveryCapability[]>
        LoadRecoveryCapabilitiesForUpdateAsync(
            Guid tenantId,
            Guid admissionTicketId,
            CancellationToken cancellationToken)
    {
        RequireTransaction();
        Guid[] capabilityIds =
            await dbContext.AdmissionRecoveryCapabilities
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.AdmissionTicketId ==
                    admissionTicketId
                    && value.ConsumedAt == null
                    && value.RotatedAt == null)
                .OrderBy(value => value.Id)
                .Select(value => value.Id)
                .ToArrayAsync(cancellationToken);
        foreach (Guid capabilityId in capabilityIds)
        {
            await RelationalEntityRowFence
                .AcquireAsync<AdmissionRecoveryCapability>(
                    dbContext,
                    tenantId,
                    capability => capability.Id,
                    capabilityId,
                    cancellationToken);
        }

        return await dbContext.AdmissionRecoveryCapabilities
            .Where(value =>
                value.TenantId == tenantId
                && value.AdmissionTicketId ==
                admissionTicketId
                && capabilityIds.Contains(value.Id)
                && value.ConsumedAt == null
                && value.RotatedAt == null)
            .OrderBy(value => value.Id)
            .ToArrayAsync(cancellationToken);
    }

    private static AdmissionTicketTransferResult Result(
        AdmissionTicketTransferOutcome outcome,
        AdmissionTicketTransfer? transfer = null,
        AdmissionTicket? ticket = null) =>
        new(outcome, transfer, ticket);
}
