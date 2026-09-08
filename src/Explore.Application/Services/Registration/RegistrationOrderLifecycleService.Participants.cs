using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed partial class RegistrationOrderLifecycleService
{
    private static RegistrationParticipant? ResolveUnitParticipant(
        RegistrationOrder order,
        EventTicketType ticketType,
        RegistrationTicketAssignment? assignment,
        ICollection<RegistrationParticipant> placeholders)
    {
        ParticipantDataCollectionModeEnum mode =
            (ParticipantDataCollectionModeEnum)
            ticketType.ParticipantDataCollectionModeId;
        if (mode == ParticipantDataCollectionModeEnum.DeferredAssignment
            && assignment?.AssignmentStatusId ==
            (int)AssignmentStatusEnum.Deferred)
        {
            return null;
        }

        if (assignment?.Participant is { } assignedParticipant)
        {
            if (assignedParticipant.Id != assignment.ParticipantId
                || assignedParticipant.TenantId != order.TenantId
                || assignedParticipant.RegistrationOrderId != order.Id
                || !RegistrationOrderRules
                    .IsParticipantEligibleForTicket(assignedParticipant))
            {
                throw new InvalidOperationException(
                    "Assigned participant is not eligible for this registration order.");
            }

            return assignedParticipant;
        }

        if (assignment?.ParticipantId is not null)
        {
            throw new InvalidOperationException(
                "Assigned participant details could not be loaded.");
        }

        RegistrationParticipant placeholder =
            RegistrationParticipant.Create(
                Guid.CreateVersion7(),
                order.TenantId,
                order.Id,
                linkedUserId: null,
                ParticipantTypeEnum.Unnamed,
                guardian: null);
        placeholders.Add(placeholder);
        return placeholder;
    }
}
