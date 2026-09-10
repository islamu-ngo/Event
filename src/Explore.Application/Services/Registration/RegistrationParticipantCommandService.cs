using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using System.Text;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationParticipantCommandService(
    IRegistrationInventoryRepository inventory,
    IRegistrationParticipantRepository participants,
    IEventTicketCatalogRepository catalogs,
    IEventSessionRepository eventSessions,
    IParticipantAdmissionEligibilityRepository eligibility,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<BaseCommandResponse<Guid>> AddAsync(
        int participantTypeId,
        Guid orderId,
        Guid? guardianParticipantId,
        ParticipantDetailsDto details,
        CancellationToken cancellationToken)
    {
        Guid participantId = Guid.CreateVersion7();
        Guid participantConcurrency = Guid.CreateVersion7();
        Guid orderConcurrency = Guid.CreateVersion7();
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenant.TenantId, token);
                if (order is null)
                {
                    return Missing(orderId);
                }

                ParticipantTypeEnum participantType = NormalizeParticipantType(participantTypeId);
                await EnsureAnonymousCollectionAsync(order, token);
                RegistrationParticipant? guardian = await GetGuardianAsync(guardianParticipantId, order, token);
                EnsureParticipantDetails(participantType, details, required: participantType is ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent);
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                if (!AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, now))
                    throw new ArgumentException("Anonymous registration data retention has expired.");

                RegistrationParticipant participant = RegistrationParticipant.Create(
                    participantId, order.TenantId, order.Id, null, participantType, guardian);
                participant.ConcurrencyStamp = participantConcurrency;
                if (HasDetails(details))
                {
                    participant.SetPii(RegistrationParticipantPii.Create(
                        participant.Id, order.TenantId, details.DisplayName, details.Email, details.Phone,
                        (int)RegistrationRetentionPolicyEnum.StandardOperational, now, order.AnonymousPiiRetentionUntilUtc));
                }

                order.BumpConcurrency(orderConcurrency);
                await participants.AddParticipantAsync(participant, token);
                await participants.SaveChangesAsync(token);
                return Success(participant.Id, "Registration participant added.");
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Invalid(orderId, exception.Message);
        }
    }

    public async Task<BaseCommandResponse<Guid>> UpdateAsync(
        Guid orderId,
        Guid participantId,
        int participantTypeId,
        Guid? guardianParticipantId,
        ParticipantDetailsDto details,
        CancellationToken cancellationToken)
    {
        Guid participantConcurrency = Guid.CreateVersion7();
        Guid orderConcurrency = Guid.CreateVersion7();
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenant.TenantId, token);
                RegistrationParticipant? participant = order is null
                    ? null
                    : await participants.GetParticipantForUpdateAsync(participantId, orderId, tenant.TenantId, token);
                if (order is null || participant is null)
                {
                    return Missing(participantId);
                }
                await EnsureAnonymousCollectionAsync(order, token);

                ParticipantTypeEnum participantType = NormalizeParticipantType(participantTypeId);
                RegistrationParticipant? guardian = await GetGuardianAsync(guardianParticipantId, order, token);
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                if (!AnonymousRegistrationRetentionPolicy.CanDisclose(order, participant.Pii?.RetentionUntil, now))
                    throw new ArgumentException("Anonymous registration data retention has expired.");

                EnsureParticipantDetails(participantType, details, required: participantType is ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent);
                participant.Update(participantType, guardian, participantConcurrency);
                if (participant.Pii is null)
                {
                    if (HasDetails(details))
                    {
                        participant.SetPii(RegistrationParticipantPii.Create(
                            participant.Id, order.TenantId, details.DisplayName, details.Email, details.Phone,
                            (int)RegistrationRetentionPolicyEnum.StandardOperational, now, order.AnonymousPiiRetentionUntilUtc));
                    }
                }
                else
                {
                    participant.Pii.Update(details.DisplayName, details.Email, details.Phone,
                        (int)RegistrationRetentionPolicyEnum.StandardOperational, now, order.AnonymousPiiRetentionUntilUtc);
                }

                order.BumpConcurrency(orderConcurrency);
                await participants.SaveChangesAsync(token);
                return Success(participant.Id, "Registration participant updated.");
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Invalid(orderId, exception.Message);
        }
    }

    public Task<BaseCommandResponse<Guid>> AssignAsync(
        Guid orderId,
        IReadOnlyCollection<TicketParticipantAssignmentInputDto> requestedAssignments,
        CancellationToken cancellationToken) => MutateAssignmentsAsync(
        orderId, requestedAssignments, [], null, null, cancellationToken);

    public async Task<BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>> ImportCompanyCsvAsync(
        Guid eventId,
        Guid orderId,
        string csvText,
        string lineageKey,
        CancellationToken cancellationToken)
    {
        string normalizedLineage = lineageKey.Trim();
        if (Encoding.UTF8.GetByteCount(csvText) > ImportCompanyRegistrationAssignmentsCsvCommandValidator.MaxCsvUtf8Bytes)
        {
            return InvalidCompany(orderId, "Company assignment CSV is too large.");
        }

        if (await participants.HasCompanyCsvAmendmentAsync(orderId, tenant.TenantId, normalizedLineage, cancellationToken))
        {
            return CompanySuccess(orderId, 0, alreadyApplied: true, "Company assignment CSV was already applied.");
        }

        if (!TryParseCompanyCsv(csvText, out CompanyRegistrationAssignmentInputDto[] rows, out string? error))
        {
            return InvalidCompany(orderId, error!);
        }

        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenant.TenantId, cancellationToken);
        if (initialOrder is null)
        {
            return InvalidCompany(orderId, "Registration order was not found.", "registration_order_not_found");
        }

        if (initialOrder.EventId != eventId)
        {
            return InvalidCompany(orderId, "Registration order was not found.", "registration_order_not_found");
        }

        if (initialOrder.BookingPartyTypeId != (int)BookingPartyTypeEnum.Company)
        {
            return InvalidCompany(orderId, "Company assignment CSV can only be applied to company registration orders.");
        }

        EventTicketCatalogVersion? initialCatalog = await catalogs.GetOrderCatalogAsync(
            initialOrder.TicketCatalogVersionId, initialOrder.EventId, tenant.TenantId, cancellationToken);
        if (initialCatalog is null)
        {
            return InvalidCompany(orderId, "Registration order catalog was not found.", "registration_order_not_found");
        }

        List<EventSession> sessions = await eventSessions.GetSessionsByEvent(initialOrder.EventId);
        var stableAdmissionIds = new Dictionary<(Guid RegistrationOrderLineId, int Ordinal), Guid[]>();
        try
        {
            foreach (CompanyRegistrationAssignmentInputDto row in rows)
            {
                stableAdmissionIds[(row.RegistrationOrderLineId, row.Ordinal)] =
                    CreateAdmissionIds(initialOrder, initialCatalog, row.RegistrationOrderLineId, sessions);
            }
        }
        catch (InvalidOperationException)
        {
            return InvalidCompany(orderId, "Company assignment CSV references a line outside this order.");
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Guid[] participantIds = Enumerable.Range(0, rows.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] participantConcurrency = Enumerable.Range(0, rows.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] assignmentIds = Enumerable.Range(0, rows.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] assignmentConcurrency = Enumerable.Range(0, rows.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] admissionConcurrency = Enumerable.Range(0, stableAdmissionIds.Values.Sum(ids => ids.Length)).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid orderConcurrency = Guid.CreateVersion7();

        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                if (await participants.HasCompanyCsvAmendmentAsync(orderId, tenant.TenantId, normalizedLineage, token))
                {
                    return CompanySuccess(orderId, 0, alreadyApplied: true, "Company assignment CSV was already applied.");
                }

                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenant.TenantId, token);
                if (order is null || order.EventId != eventId || order.ConcurrencyStamp != initialOrder.ConcurrencyStamp)
                {
                    return InvalidCompany(orderId, "Registration order changed while company assignments were imported.");
                }

                if (order.BookingPartyTypeId != (int)BookingPartyTypeEnum.Company ||
                    (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId != RegistrationOrderStatusEnum.Confirmed)
                {
                    return InvalidCompany(orderId, "Company assignment CSV requires a confirmed company registration order.");
                }

                EventTicketCatalogVersion? catalog = await catalogs.GetOrderCatalogAsync(
                    order.TicketCatalogVersionId, order.EventId, tenant.TenantId, token);
                if (catalog is null)
                {
                    return InvalidCompany(orderId, "Registration order catalog was not found.", "registration_order_not_found");
                }

                var ticketTypes = catalog.TicketTypes.Where(ticket => !ticket.IsDeleted).ToDictionary(ticket => ticket.Id);
                var lines = order.Lines.ToDictionary(line => line.Id);
                IReadOnlyList<RegistrationTicketAssignment> existing =
                    await participants.GetAssignmentsForUpdateByOrderAsync(order.Id, order.TenantId, token);
                var byKey = existing.ToDictionary(item => (item.RegistrationOrderLineId, item.Ordinal));
                var newParticipants = new List<RegistrationParticipant>();
                var newAssignments = new List<RegistrationTicketAssignment>();
                var amendments = new List<RegistrationAmendment>();
                int admissionIndex = 0;

                for (int index = 0; index < rows.Length; index++)
                {
                    CompanyRegistrationAssignmentInputDto row = rows[index];
                    if (!TryResolveLine(row.RegistrationOrderLineId, row.Ordinal, lines, ticketTypes, out RegistrationOrderLine? line, out EventTicketType? ticket, out string? lineError))
                    {
                        return InvalidCompany(orderId, lineError!);
                    }

                    ParticipantDataCollectionModeEnum mode = NormalizeCollectionMode(ticket!.ParticipantDataCollectionModeId);
                    if (mode is ParticipantDataCollectionModeEnum.None or ParticipantDataCollectionModeEnum.LeadBookerOnly || ticket.RequiresGuardian)
                    {
                        return InvalidCompany(orderId, "Company assignment CSV can only assign direct per-ticket or deferred ticket units.");
                    }

                    ParticipantTypeEnum type = NormalizeParticipantType(row.ParticipantTypeId);
                    EnsureParticipantDetails(type, new ParticipantDetailsDto(row.DisplayName, row.Email, row.Phone), required: true);
                    DateTime piiCreatedAt = timeProvider.GetUtcNow().UtcDateTime;
                    if (!AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, piiCreatedAt))
                        throw new ArgumentException("Anonymous registration data retention has expired.");

                    RegistrationParticipant participant = RegistrationParticipant.Create(
                        participantIds[index], order.TenantId, order.Id, null, type, null);
                    participant.ConcurrencyStamp = participantConcurrency[index];
                    participant.SetPii(RegistrationParticipantPii.Create(participant.Id, order.TenantId, row.DisplayName, row.Email, row.Phone,
                        (int)RegistrationRetentionPolicyEnum.StandardOperational, piiCreatedAt, order.AnonymousPiiRetentionUntilUtc));
                    newParticipants.Add(participant);

                    RegistrationTicketAssignment? previous = byKey.GetValueOrDefault((line!.Id, row.Ordinal));
                    if (previous is null)
                    {
                        RegistrationTicketAssignment assignment = RegistrationTicketAssignment.CreateAssigned(
                            assignmentIds[index], line.Id, row.Ordinal, participant, now);
                        assignment.ConcurrencyStamp = assignmentConcurrency[index];
                        newAssignments.Add(assignment);
                        byKey[(line.Id, row.Ordinal)] = assignment;
                    }
                    else
                    {
                        previous.Assign(participant, assignmentConcurrency[index]);
                    }

                    IReadOnlyList<EventRegistration> admissions = await participants.GetAdmissionsForUpdateAsync(
                        order.Id, line.Id, row.Ordinal, order.TenantId, token);
                    if (admissions.Count > 0)
                    {
                        foreach (EventRegistration admission in admissions.Where(value => value.RegistrationParticipantId != participant.Id))
                        {
                            admission.ReassignParticipant(participant, admissionConcurrency[admissionIndex++]);
                        }
                    }
                    else
                    {
                        IReadOnlyList<(TicketTypeEntitlement Entitlement, EventSession Session)> expanded =
                            RegistrationAdmissionMaterializer.Expand(ticket, sessions);
                        Guid[] stableIds = stableAdmissionIds[(line.Id, row.Ordinal)];
                        EventRegistration[] materialized = expanded.Select((value, admissionOrdinal) =>
                            RegistrationAdmissionMaterializer.Create(
                                stableIds[admissionOrdinal], admissionConcurrency[admissionIndex++], order, line, value.Entitlement,
                                value.Session, participant, row.Ordinal, now)).ToArray();
                        await inventory.AddEventRegistrationsAsync(materialized, token);
                    }

                    amendments.Add(RegistrationAmendment.CreateCompanyCsvAssignmentChange(
                        order.TenantId, order.EventId, order.Id, currentUser.UserId, normalizedLineage, line.Id, row.Ordinal,
                        previous?.ParticipantId, previous?.AssignmentStatusId, participant.Id, (int)AssignmentStatusEnum.Assigned, now));
                }

                order.BumpConcurrency(orderConcurrency);
                foreach (RegistrationParticipant participant in newParticipants)
                {
                    await participants.AddParticipantAsync(participant, token);
                }
                await participants.AddAssignmentsAsync(newAssignments, token);
                await participants.AddAmendmentsAsync(amendments, token);
                await participants.SaveChangesAsync(token);
                await eligibility.EnsureForAssignmentsAsync(
                    order.TenantId,
                    order.EventId,
                    order.Id,
                    newAssignments
                        .Select(assignment => assignment.Id)
                        .ToArray(),
                    now,
                    token);
                return CompanySuccess(order.Id, rows.Length, alreadyApplied: false, "Company assignment CSV imported.");
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return InvalidCompany(orderId, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return InvalidCompany(orderId, exception.Message);
        }
    }

    public Task<BaseCommandResponse<Guid>> DeferAsync(
        Guid orderId,
        IReadOnlyCollection<TicketDeferralInputDto> requestedDeferrals,
        DateTime deadline,
        CancellationToken cancellationToken) => MutateAssignmentsAsync(
        orderId, [], requestedDeferrals, deadline, null, cancellationToken);

    private async Task<BaseCommandResponse<Guid>> MutateAssignmentsAsync(
        Guid orderId,
        IReadOnlyCollection<TicketParticipantAssignmentInputDto> requestedAssignments,
        IReadOnlyCollection<TicketDeferralInputDto> requestedDeferrals,
        DateTime? deadline,
        string? amendmentReason,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (deadline.HasValue && (deadline.Value.Kind != DateTimeKind.Utc || deadline <= now))
        {
            return Invalid(orderId, "Assignment deadline must be a future UTC timestamp.");
        }

        var requestedKeys = requestedAssignments.Select(item => (item.RegistrationOrderLineId, item.Ordinal))
            .Concat(requestedDeferrals.Select(item => (item.RegistrationOrderLineId, item.Ordinal))).ToArray();
        if (requestedKeys.Length == 0 || requestedKeys.Distinct().Count() != requestedKeys.Length)
        {
            return Invalid(orderId, "Ticket assignment payload contains duplicate or missing ordinals.");
        }

        if (requestedAssignments.Select(item => item.ParticipantId).Distinct().Count() != requestedAssignments.Count)
        {
            return Invalid(orderId, "A participant cannot occupy more than one ticket ordinal in the same order.");
        }

        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenant.TenantId, cancellationToken);
        if (initialOrder is null)
        {
            return Missing(orderId);
        }

        EventTicketCatalogVersion? initialCatalog = await catalogs.GetOrderCatalogAsync(
            initialOrder.TicketCatalogVersionId, initialOrder.EventId, tenant.TenantId, cancellationToken);
        if (initialCatalog is null)
        {
            return Missing(orderId);
        }

        List<EventSession> sessions = await eventSessions.GetSessionsByEvent(initialOrder.EventId);
        Dictionary<(Guid RegistrationOrderLineId, int Ordinal), Guid[]> admissionIds;
        try
        {
            admissionIds = requestedAssignments.ToDictionary(
                item => (item.RegistrationOrderLineId, item.Ordinal),
                item => CreateAdmissionIds(initialOrder, initialCatalog, item.RegistrationOrderLineId, sessions));
        }
        catch (InvalidOperationException)
        {
            return Invalid(orderId, "Ticket assignment line does not belong to this order.");
        }
        Guid[] assignmentIds = Enumerable.Range(0, requestedKeys.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] assignmentConcurrency = Enumerable.Range(0, requestedKeys.Length).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] admissionConcurrency = Enumerable.Range(0, admissionIds.Values.Sum(ids => ids.Length)).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid orderConcurrency = Guid.CreateVersion7();

        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenant.TenantId, token);
                if (order is null || order.ConcurrencyStamp != initialOrder.ConcurrencyStamp)
                {
                    return Conflict(orderId);
                }

                EventTicketCatalogVersion? catalog = await catalogs.GetOrderCatalogAsync(
                    order.TicketCatalogVersionId, order.EventId, tenant.TenantId, token);
                if (catalog is null)
                {
                    return Missing(orderId);
                }

                var ticketTypes = catalog.TicketTypes.Where(ticket => !ticket.IsDeleted).ToDictionary(ticket => ticket.Id);
                var lines = order.Lines.ToDictionary(line => line.Id);
                IReadOnlyList<RegistrationTicketAssignment> existing =
                    await participants.GetAssignmentsForUpdateByOrderAsync(order.Id, order.TenantId, token);
                var byKey = existing.ToDictionary(item => (item.RegistrationOrderLineId, item.Ordinal));
                var additions = new List<RegistrationTicketAssignment>();
                var amendments = new List<RegistrationAmendment>();
                bool changed = false;
                int mutationIndex = 0;
                int admissionIndex = 0;
                bool isConfirmed = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId == RegistrationOrderStatusEnum.Confirmed;

                foreach (TicketParticipantAssignmentInputDto item in requestedAssignments)
                {
                    if (!TryResolveLine(item.RegistrationOrderLineId, item.Ordinal, lines, ticketTypes, out RegistrationOrderLine? line, out EventTicketType? ticket, out string? error))
                    {
                        return Invalid(orderId, error!);
                    }

                    ParticipantDataCollectionModeEnum mode = NormalizeCollectionMode(ticket!.ParticipantDataCollectionModeId);
                    if (mode is ParticipantDataCollectionModeEnum.None or ParticipantDataCollectionModeEnum.LeadBookerOnly)
                    {
                        return Invalid(orderId, "The ticket data-collection mode does not permit participant assignments.");
                    }

                    RegistrationParticipant? participant = await participants.GetParticipantForUpdateAsync(
                        item.ParticipantId, order.Id, order.TenantId, token);
                    if (participant is null || !RegistrationOrderRules.IsParticipantEligibleForTicket(participant) ||
                        ticket.RequiresGuardian && participant.GuardianParticipantId is null)
                    {
                        return Invalid(orderId, "The participant is not eligible for this ticket assignment.");
                    }

                    EnsureParticipantDetails((ParticipantTypeEnum)participant.ParticipantTypeId,
                        new ParticipantDetailsDto(participant.Pii?.DisplayName, participant.Pii?.Email, participant.Pii?.Phone),
                        required: mode == ParticipantDataCollectionModeEnum.PerTicketRequired || ticket.RequiresGuardian);
                    if (existing.Any(assignment => assignment.ParticipantId == participant.Id &&
                        (assignment.RegistrationOrderLineId, assignment.Ordinal) != (item.RegistrationOrderLineId, item.Ordinal)))
                    {
                        return Invalid(orderId, "A participant cannot occupy more than one ticket ordinal in the same order.");
                    }

                    Guid? beforeParticipantId = null;
                    int? beforeStatusId = null;
                    bool assignmentChanged = false;
                    if (byKey.TryGetValue((line!.Id, item.Ordinal), out RegistrationTicketAssignment? assignment))
                    {
                        beforeParticipantId = assignment.ParticipantId;
                        beforeStatusId = assignment.AssignmentStatusId;
                        if (assignment.AssignmentStatusId != (int)AssignmentStatusEnum.Assigned || assignment.ParticipantId != participant.Id)
                        {
                            assignment.Assign(participant, assignmentConcurrency[mutationIndex]);
                            assignmentChanged = true;
                            changed = true;
                        }
                    }
                    else
                    {
                        assignment = RegistrationTicketAssignment.CreateAssigned(
                            assignmentIds[mutationIndex], line.Id, item.Ordinal, participant, now);
                        assignment.ConcurrencyStamp = assignmentConcurrency[mutationIndex];
                        additions.Add(assignment);
                        assignmentChanged = true;
                        changed = true;
                        byKey[(line.Id, item.Ordinal)] = assignment;
                    }

                    if (isConfirmed)
                    {
                        if (assignmentChanged && string.IsNullOrWhiteSpace(amendmentReason))
                        {
                            return Invalid(orderId, "Finalized registration assignment changes require an amendment reason.");
                        }

                        IReadOnlyList<EventRegistration> admissions = await participants.GetAdmissionsForUpdateAsync(
                            order.Id, line.Id, item.Ordinal, order.TenantId, token);
                        if (admissions.Count > 0)
                        {
                            foreach (EventRegistration admission in admissions.Where(value => value.RegistrationParticipantId != participant.Id))
                            {
                                admission.ReassignParticipant(participant, admissionConcurrency[admissionIndex++]);
                                changed = true;
                            }
                        }
                        else
                        {
                            IReadOnlyList<(TicketTypeEntitlement Entitlement, EventSession Session)> expanded =
                                RegistrationAdmissionMaterializer.Expand(ticket, sessions);
                            Guid[] stableIds = admissionIds[(line.Id, item.Ordinal)];
                            EventRegistration[] materialized = expanded.Select((value, index) =>
                                RegistrationAdmissionMaterializer.Create(
                                    stableIds[index], admissionConcurrency[admissionIndex++], order, line, value.Entitlement,
                                    value.Session, participant, item.Ordinal, now)).ToArray();
                            await inventory.AddEventRegistrationsAsync(materialized, token);
                            changed = materialized.Length > 0 || changed;
                        }

                        if (assignmentChanged)
                        {
                            amendments.Add(RegistrationAmendment.CreateAssignmentChange(
                                order.TenantId, order.EventId, order.Id, currentUser.UserId, amendmentReason!, line.Id,
                                item.Ordinal, beforeParticipantId, beforeStatusId, participant.Id,
                                (int)AssignmentStatusEnum.Assigned, now));
                        }
                    }

                    mutationIndex++;
                }

                foreach (TicketDeferralInputDto item in requestedDeferrals)
                {
                    if (!TryResolveLine(item.RegistrationOrderLineId, item.Ordinal, lines, ticketTypes, out RegistrationOrderLine? line, out EventTicketType? ticket, out string? error))
                    {
                        return Invalid(orderId, error!);
                    }

                    if ((RegistrationOrderStatusEnum)order.RegistrationOrderStatusId == RegistrationOrderStatusEnum.Confirmed ||
                        NormalizeCollectionMode(ticket!.ParticipantDataCollectionModeId) != ParticipantDataCollectionModeEnum.DeferredAssignment)
                    {
                        return Invalid(orderId, "Only unconfirmed deferred-assignment tickets can be deferred.");
                    }

                    if (byKey.TryGetValue((line!.Id, item.Ordinal), out RegistrationTicketAssignment? assignment))
                    {
                        if (assignment.AssignmentStatusId != (int)AssignmentStatusEnum.Deferred || assignment.AssignmentDeadline != deadline)
                        {
                            assignment.Defer(deadline!.Value, now, assignmentConcurrency[mutationIndex]);
                            changed = true;
                        }
                    }
                    else
                    {
                        assignment = RegistrationTicketAssignment.Create(
                            assignmentIds[mutationIndex], order.TenantId, order.Id, line.Id, item.Ordinal, null,
                            AssignmentStatusEnum.Deferred, deadline, now);
                        assignment.ConcurrencyStamp = assignmentConcurrency[mutationIndex];
                        additions.Add(assignment);
                        changed = true;
                        byKey[(line.Id, item.Ordinal)] = assignment;
                    }

                    mutationIndex++;
                }

                if (!changed)
                {
                    return Success(order.Id, "Registration ticket assignments already match the request.");
                }

                order.BumpConcurrency(orderConcurrency);
                await participants.AddAssignmentsAsync(additions, token);
                await participants.AddAmendmentsAsync(amendments, token);
                await participants.SaveChangesAsync(token);
                await eligibility.EnsureForAssignmentsAsync(
                    order.TenantId,
                    order.EventId,
                    order.Id,
                    byKey.Values
                        .Where(assignment =>
                            assignment.ParticipantId.HasValue)
                        .Select(assignment => assignment.Id)
                        .ToArray(),
                    now,
                    token);
                return Success(order.Id, "Registration ticket assignments updated.");
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Invalid(orderId, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(orderId, exception.Message);
        }
    }

    private async Task EnsureAnonymousCollectionAsync(RegistrationOrder order, CancellationToken cancellationToken)
    {
        if (!AnonymousRegistrationRetentionPolicy.AppliesTo(order)) return;
        if (order.AnonymousPiiRetentionUntilUtc is null)
            throw new ArgumentException("The original anonymous retention bound is unavailable.");

        EventTicketCatalogVersion? catalog = await catalogs.GetOrderCatalogAsync(
            order.TicketCatalogVersionId, order.EventId, order.TenantId, cancellationToken);
        if (catalog is null || !order.Lines.Any(line => catalog.TicketTypes.Any(ticket =>
            ticket.Id == line.TicketTypeId && !ticket.IsDeleted &&
            ticket.ParticipantDataCollectionModeId != (int)ParticipantDataCollectionModeEnum.None)))
        {
            throw new ArgumentException("The pinned ticket collection policy does not collect participant details.");
        }
    }

    private async Task<RegistrationParticipant?> GetGuardianAsync(
        Guid? guardianParticipantId,
        RegistrationOrder order,
        CancellationToken cancellationToken) => guardianParticipantId.HasValue
        ? await participants.GetParticipantForUpdateAsync(guardianParticipantId.Value, order.Id, order.TenantId, cancellationToken)
            ?? throw new ArgumentException("Guardian participant was not found in this order.")
        : null;

    private static Guid[] CreateAdmissionIds(
        RegistrationOrder order,
        EventTicketCatalogVersion catalog,
        Guid lineId,
        IReadOnlyCollection<EventSession> sessions)
    {
        RegistrationOrderLine line = order.Lines.Single(value => value.Id == lineId);
        EventTicketType ticket = catalog.TicketTypes.Single(value => value.Id == line.TicketTypeId);
        return RegistrationAdmissionMaterializer.Expand(ticket, sessions).Select(_ => Guid.CreateVersion7()).ToArray();
    }

    private static bool TryResolveLine(
        Guid lineId,
        int ordinal,
        IReadOnlyDictionary<Guid, RegistrationOrderLine> lines,
        IReadOnlyDictionary<Guid, EventTicketType> ticketTypes,
        out RegistrationOrderLine? line,
        out EventTicketType? ticket,
        out string? error)
    {
        ticket = null;
        if (!lines.TryGetValue(lineId, out line) || !ticketTypes.TryGetValue(line.TicketTypeId, out ticket))
        {
            error = "Ticket assignment line does not belong to this order.";
            return false;
        }

        if (ordinal < 1 || ordinal > line.Quantity)
        {
            error = "Ticket assignment ordinal is outside the order-line quantity.";
            return false;
        }

        error = null;
        return true;
    }

    private static ParticipantTypeEnum NormalizeParticipantType(int participantTypeId)
    {
        ParticipantTypeEnum type = (ParticipantTypeEnum)participantTypeId;
        return Enum.IsDefined(type) && type != ParticipantTypeEnum.Unnamed
            ? type
            : throw new ArgumentException("Participant type is invalid.");
    }

    private static ParticipantDataCollectionModeEnum NormalizeCollectionMode(int modeId)
    {
        ParticipantDataCollectionModeEnum mode = (ParticipantDataCollectionModeEnum)modeId;
        return Enum.IsDefined(mode) ? mode : throw new ArgumentException("Participant data-collection mode is invalid.");
    }

    private static void EnsureParticipantDetails(ParticipantTypeEnum type, ParticipantDetailsDto details, bool required)
    {
        ArgumentNullException.ThrowIfNull(details);
        if ((required || type is ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent) &&
            string.IsNullOrWhiteSpace(details.DisplayName))
        {
            throw new ArgumentException("Required participant details are incomplete.");
        }
    }

    private static bool HasDetails(ParticipantDetailsDto details) =>
        !string.IsNullOrWhiteSpace(details.DisplayName) || !string.IsNullOrWhiteSpace(details.Email) || !string.IsNullOrWhiteSpace(details.Phone);

    private static bool TryParseCompanyCsv(
        string csvText,
        out CompanyRegistrationAssignmentInputDto[] assignments,
        out string? error)
    {
        assignments = [];
        error = null;
        string[] lines = csvText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is < 2 or > 1001 || lines[0] != "registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone")
        {
            error = "Company assignment CSV is invalid.";
            return false;
        }

        var parsed = new List<CompanyRegistrationAssignmentInputDto>(lines.Length - 1);
        var assignmentKeys = new HashSet<(Guid RegistrationOrderLineId, int Ordinal)>();
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',', StringSplitOptions.TrimEntries);
            if (cells.Length != 6 || cells.Any(IsFormulaCell) ||
                !Guid.TryParse(cells[0], out Guid lineId) || !int.TryParse(cells[1], out int ordinal) ||
                !int.TryParse(cells[2], out int participantTypeId) || string.IsNullOrWhiteSpace(cells[3]) ||
                !assignmentKeys.Add((lineId, ordinal)))
            {
                error = "Company assignment CSV is invalid.";
                return false;
            }

            parsed.Add(new CompanyRegistrationAssignmentInputDto(lineId, ordinal, participantTypeId, cells[3], NullIfEmpty(cells[4]), NullIfEmpty(cells[5])));
        }

        assignments = parsed.ToArray();
        return assignments.Length > 0;

        static bool IsFormulaCell(string value) => value.Length > 0 && value[0] is '=' or '+' or '-' or '@';
        static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);
    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Validation(
        ["Registration participant resource was not found."], "Registration participant resource was not found.", id);
    private static BaseCommandResponse<Guid> Invalid(Guid id, string error) => BaseCommandResponse.Validation(
        [error], "Registration participant request is invalid.", id);
    private static BaseCommandResponse<Guid> Conflict(Guid id) => BaseCommandResponse.Validation(
        ["Registration order changed while assignments were updated."],
        "Registration order changed while assignments were updated.", id);
    private static BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> CompanySuccess(Guid id, int count, bool alreadyApplied, string message) =>
        BaseCommandResponse.Success(new CompanyRegistrationAssignmentCsvResultDto(id, count, alreadyApplied), message);

    private static BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> InvalidCompany(Guid id, string error, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(
                [error], "Company assignment CSV request is invalid.",
                new CompanyRegistrationAssignmentCsvResultDto(id, 0, false))
            : BaseCommandResponse.Failure<CompanyRegistrationAssignmentCsvResultDto>(
                failureCode, "Company assignment CSV request is invalid.", [error],
                new CompanyRegistrationAssignmentCsvResultDto(id, 0, false));
}
