using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ParticipantAdmissionEligibilityRepository(
    ExploreDbContext dbContext) :
    IParticipantAdmissionEligibilityRepository
{
    public const string CanonicalFenceOrder =
        "assignment>eligibility>ticket>transfer";

    public Task<ParticipantAdmissionEligibility?> GetAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken) =>
        dbContext.ParticipantAdmissionEligibilities
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.RegistrationTicketAssignmentId ==
                registrationTicketAssignmentId,
                cancellationToken);

    public Task<AdmissionTicket?> GetIssuedTicketAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken) =>
        dbContext.AdmissionTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.RegistrationTicketAssignmentId ==
                registrationTicketAssignmentId,
                cancellationToken);

    public async Task<ParticipantAdmissionEligibility?>
        LoadForUpdateAsync(
            Guid tenantId,
            Guid registrationTicketAssignmentId,
            CancellationToken cancellationToken)
    {
        RequireTransaction();
        await RelationalEntityRowFence
            .AcquireAsync<RegistrationTicketAssignment>(
                dbContext,
                tenantId,
                assignment => assignment.Id,
                registrationTicketAssignmentId,
                cancellationToken);
        return await dbContext
            .ParticipantAdmissionEligibilities
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.RegistrationTicketAssignmentId ==
                registrationTicketAssignmentId,
                cancellationToken);
    }

    public async Task AddAsync(
        ParticipantAdmissionEligibility eligibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        RequireTransaction();
        await RelationalEntityRowFence
            .AcquireAsync<RegistrationTicketAssignment>(
                dbContext,
                eligibility.TenantId,
                assignment => assignment.Id,
                eligibility.RegistrationTicketAssignmentId,
                cancellationToken);
        await dbContext.ParticipantAdmissionEligibilities
            .AddAsync(eligibility, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureForAssignmentsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        IReadOnlyCollection<Guid> assignmentIds,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignmentIds);
        RequireTransaction();
        Guid[] ids = assignmentIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        foreach (Guid assignmentId in ids)
        {
            await RelationalEntityRowFence
                .AcquireAsync<RegistrationTicketAssignment>(
                    dbContext,
                    tenantId,
                    assignment => assignment.Id,
                    assignmentId,
                    cancellationToken);
        }
        HashSet<Guid> existing =
            await dbContext.ParticipantAdmissionEligibilities
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && ids.Contains(
                        value.RegistrationTicketAssignmentId))
                .Select(value =>
                    value.RegistrationTicketAssignmentId)
                .ToHashSetAsync(cancellationToken);
        RegistrationTicketAssignment[] assignments =
            await dbContext.RegistrationTicketAssignments
                .Include(value => value.Participant)
                .Where(value =>
                    value.TenantId == tenantId
                    && value.RegistrationOrderId ==
                    registrationOrderId
                    && ids.Contains(value.Id)
                    && value.ParticipantId.HasValue)
                .ToArrayAsync(cancellationToken);
        Guid[] lineIds = assignments
            .Select(value => value.RegistrationOrderLineId)
            .Distinct()
            .ToArray();
        Dictionary<Guid, Guid> ticketTypeByLine =
            await dbContext.RegistrationOrderLines
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.RegistrationOrderId ==
                    registrationOrderId
                    && lineIds.Contains(value.Id))
                .ToDictionaryAsync(
                    value => value.Id,
                    value => value.TicketTypeId,
                    cancellationToken);
        Guid[] ticketTypeIds =
            ticketTypeByLine.Values.Distinct().ToArray();
        Dictionary<Guid, EventTicketType> ticketTypes =
            await dbContext.EventTicketTypes
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && ticketTypeIds.Contains(value.Id))
                .ToDictionaryAsync(
                    value => value.Id,
                    cancellationToken);
        var additions =
            new List<ParticipantAdmissionEligibility>();
        foreach (RegistrationTicketAssignment assignment
                 in assignments)
        {
            if (existing.Contains(assignment.Id)
                || assignment.Participant is null
                || !ticketTypeByLine.TryGetValue(
                    assignment.RegistrationOrderLineId,
                    out Guid ticketTypeId)
                || !ticketTypes.TryGetValue(
                    ticketTypeId,
                    out EventTicketType? ticketType))
            {
                continue;
            }

            ParticipantDataCollectionModeEnum collectionMode =
                (ParticipantDataCollectionModeEnum)
                ticketType.ParticipantDataCollectionModeId;
            bool consentRequired = collectionMode is
                ParticipantDataCollectionModeEnum
                    .PerTicketRequired
                or ParticipantDataCollectionModeEnum
                    .DeferredAssignment;
            additions.Add(
                ParticipantAdmissionEligibility.Create(
                    tenantId,
                    eventId,
                    assignment,
                    assignment.Participant,
                    consentRequired,
                    ticketType.RequiresApproval,
                    createdAt));
        }
        if (additions.Count == 0)
        {
            return;
        }

        await dbContext.ParticipantAdmissionEligibilities
            .AddRangeAsync(additions, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ParticipantAdmissionCompletionContext?>
        LoadCompletionForUpdateAsync(
            Guid tenantId,
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationTicketAssignmentId,
            Guid participantId,
            Guid subjectUserId,
            CancellationToken cancellationToken)
    {
        ParticipantAdmissionEligibility? eligibility =
            await LoadForUpdateAsync(
                tenantId,
                registrationTicketAssignmentId,
                cancellationToken);
        if (eligibility is null
            || eligibility.EventId != eventId
            || eligibility.RegistrationOrderId !=
            registrationOrderId
            || eligibility.ParticipantId != participantId)
        {
            return null;
        }

        RegistrationParticipant? participant =
            await dbContext.RegistrationParticipants
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.RegistrationOrderId ==
                    registrationOrderId
                    && value.Id == participantId,
                    cancellationToken);
        if (participant is null)
        {
            return null;
        }
        if (participant.LinkedUserId.HasValue
            && participant.LinkedUserId != subjectUserId)
        {
            return null;
        }
        if (!participant.LinkedUserId.HasValue)
        {
            Guid? purchaserUserId = await dbContext
                .RegistrationOrders
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == registrationOrderId)
                .Select(value => value.AccountUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (purchaserUserId != subjectUserId)
            {
                return null;
            }
        }

        Guid? workflowId = await dbContext
            .RegistrationOrders
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == registrationOrderId)
            .Select(value =>
                value.RegistrationWorkflowVersionId)
            .SingleOrDefaultAsync(cancellationToken);
        bool requirementsComplete =
            await AreSubjectRequirementsCompleteAsync(
                tenantId,
                workflowId,
                registrationOrderId,
                eligibility.RegistrationOrderLineId,
                registrationTicketAssignmentId,
                participant,
                subjectUserId,
                cancellationToken);
        Guid? consentRecordId = eligibility.ConsentRequired
            ? await dbContext.RegistrationConsentRecords
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.RegistrationOrderId ==
                    registrationOrderId
                    && workflowId.HasValue
                    && value.RegistrationWorkflowId ==
                    workflowId.Value
                    && value.ParticipantSubjectId ==
                    participantId
                    && value.CreatedBy == subjectUserId
                    && value.WithdrawnAt == null)
                .OrderByDescending(value => value.GrantedAt)
                .ThenByDescending(value => value.Id)
                .Select(value => (Guid?)value.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return new ParticipantAdmissionCompletionContext(
            eligibility,
            participant,
            requirementsComplete,
            consentRecordId);
    }

    public async Task<AdmissionTicket?>
        GetIssuedTicketForUpdateAsync(
            Guid tenantId,
            Guid registrationTicketAssignmentId,
            CancellationToken cancellationToken)
    {
        RequireTransaction();
        AdmissionTicket? ticket =
            await dbContext.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.RegistrationTicketAssignmentId ==
                    registrationTicketAssignmentId,
                    cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        await RelationalEntityRowFence
            .AcquireAsync<AdmissionTicket>(
                dbContext,
                tenantId,
                ticket => ticket.Id,
                ticket.Id,
                cancellationToken);
        await dbContext.Entry(ticket)
            .ReloadAsync(cancellationToken);
        return ticket;
    }

    public async Task ApplyDecisionAsync(
        ParticipantAdmissionEligibility eligibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        RequireTransaction();
        if (dbContext.Entry(eligibility).State ==
            EntityState.Detached)
        {
            throw new InvalidOperationException(
                "Eligibility must be loaded under its assignment fence.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ParticipantAdmissionReadinessDecision?>
        EvaluateForUpdateAsync(
            Guid tenantId,
            Guid registrationTicketAssignmentId,
            bool orderConfirmed,
            bool paymentSatisfied,
            CancellationToken cancellationToken)
    {
        ParticipantAdmissionEligibility? eligibility =
            await LoadForUpdateAsync(
                tenantId,
                registrationTicketAssignmentId,
                cancellationToken);
        return eligibility?.DescribeReadiness(
            orderConfirmed,
            paymentSatisfied);
    }

    public async Task<ParticipantAdmissionTransferReadiness?>
        ResolveTransferRecipientReadinessAsync(
            ParticipantAdmissionEligibility eligibility,
            RegistrationParticipant recipient,
            Guid recipientSubjectUserId,
            Guid? expectedConsentRecordId,
            Guid? approvedByActorId,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(recipient);
        RequireTransaction();
        if (dbContext.Entry(eligibility).State ==
                EntityState.Detached
            || eligibility.TenantId != recipient.TenantId
            || eligibility.RegistrationOrderId !=
            recipient.RegistrationOrderId
            || recipient.LinkedUserId !=
            recipientSubjectUserId)
        {
            return null;
        }

        Guid? workflowId = await dbContext
            .RegistrationOrders
            .AsNoTracking()
            .Where(value =>
                value.TenantId == eligibility.TenantId
                && value.EventId == eligibility.EventId
                && value.Id ==
                eligibility.RegistrationOrderId)
            .Select(value =>
                value.RegistrationWorkflowVersionId)
            .SingleOrDefaultAsync(cancellationToken);
        bool requirementsComplete =
            await AreSubjectRequirementsCompleteAsync(
                eligibility.TenantId,
                workflowId,
                eligibility.RegistrationOrderId,
                eligibility.RegistrationOrderLineId,
                eligibility
                    .RegistrationTicketAssignmentId,
                recipient,
                recipientSubjectUserId,
                cancellationToken);
        Guid? consentRecordId = eligibility.ConsentRequired
            ? await dbContext.RegistrationConsentRecords
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == eligibility.TenantId
                    && value.EventId == eligibility.EventId
                    && value.RegistrationOrderId ==
                    eligibility.RegistrationOrderId
                    && workflowId.HasValue
                    && value.RegistrationWorkflowId ==
                    workflowId.Value
                    && value.ParticipantSubjectId ==
                    recipient.Id
                    && value.CreatedBy ==
                    recipientSubjectUserId
                    && value.WithdrawnAt == null)
                .OrderByDescending(value => value.GrantedAt)
                .ThenByDescending(value => value.Id)
                .Select(value => (Guid?)value.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        if (expectedConsentRecordId.HasValue
            && expectedConsentRecordId != consentRecordId)
        {
            return null;
        }

        Guid? approvalActorId = null;
        if (approvedByActorId.HasValue)
        {
            approvalActorId = await dbContext.Actors
                .AsNoTracking()
                .Where(value =>
                    value.Id == approvedByActorId.Value)
                .Select(value => (Guid?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (!approvalActorId.HasValue)
            {
                return null;
            }
        }

        return new ParticipantAdmissionTransferReadiness(
            requirementsComplete,
            consentRecordId,
            approvalActorId);
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.IsRelational()
            && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Participant admission readiness requires an active transaction.");
        }
    }

    private async Task<bool>
        AreSubjectRequirementsCompleteAsync(
            Guid tenantId,
            Guid? workflowId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid registrationTicketAssignmentId,
            RegistrationParticipant participant,
            Guid subjectUserId,
            CancellationToken cancellationToken)
    {
        if (!workflowId.HasValue)
        {
            return true;
        }

        int[] subjectTypes =
            participant.ParticipantTypeId is
                (int)ParticipantTypeEnum.Child
                or (int)ParticipantTypeEnum.Dependent
                ? [
                    (int)RegistrationRequirementSubjectTypeEnum
                        .EveryParticipant,
                    (int)RegistrationRequirementSubjectTypeEnum
                        .ChildParticipants,
                    (int)RegistrationRequirementSubjectTypeEnum
                        .SpecificTicketType,
                ]
                : [
                    (int)RegistrationRequirementSubjectTypeEnum
                        .EveryParticipant,
                    (int)RegistrationRequirementSubjectTypeEnum
                        .SpecificTicketType,
                ];
        Guid? ticketTypeId = await dbContext
            .RegistrationOrderLines
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId
                && value.RegistrationOrderId ==
                registrationOrderId
                && value.Id == registrationOrderLineId)
            .Select(value => (Guid?)value.TicketTypeId)
            .SingleOrDefaultAsync(cancellationToken);
        Guid[] requirementIds =
            await dbContext.RegistrationRequirements
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.RegistrationWorkflowId ==
                    workflowId.Value
                    && value.CriticalityId ==
                    (int)RegistrationRequirementCriticalityEnum
                        .Required
                    && subjectTypes.Contains(
                        value.AppliesToSubjectTypeId)
                    && (value.AppliesToSubjectTypeId !=
                        (int)RegistrationRequirementSubjectTypeEnum
                            .SpecificTicketType
                        || value.AppliesToSubjectId ==
                        ticketTypeId))
                .Select(value => value.Id)
                .ToArrayAsync(cancellationToken);
        if (requirementIds.Length == 0)
        {
            return true;
        }

        Guid[] fulfilledRequirementIds =
            await (
                from fulfillment in dbContext
                    .RegistrationRequirementFulfillments
                    .AsNoTracking()
                join submission in dbContext
                    .RegistrationSubmissions
                    .AsNoTracking()
                    on new
                    {
                        fulfillment.TenantId,
                        Id = fulfillment
                            .SourceRegistrationSubmissionId,
                    }
                    equals new
                    {
                        submission.TenantId,
                        Id = (Guid?)submission.Id,
                    }
                where fulfillment.TenantId == tenantId
                    && fulfillment.RegistrationOrderId ==
                    registrationOrderId
                    && requirementIds.Contains(
                        fulfillment.RegistrationRequirementId)
                    && !fulfillment.IsSkipped
                    && submission.CreatedBy == subjectUserId
                    && submission.IsFinalizable
                    && (fulfillment.SubjectTypeId ==
                        (int)RegistrationAnswerSubjectTypeEnum
                            .Participant
                        && fulfillment.SubjectId ==
                        participant.Id
                        || fulfillment.SubjectTypeId ==
                        (int)RegistrationAnswerSubjectTypeEnum
                            .TicketAssignment
                        && fulfillment.SubjectId ==
                        registrationTicketAssignmentId)
                select fulfillment.RegistrationRequirementId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        return requirementIds
            .All(fulfilledRequirementIds.Contains);
    }
}
