
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Handlers;

namespace Explore.Application.Services.Registration;

public enum AnonymousCancellationOutcome { InvalidAuthority, Ineligible, Eligible, Cancelled }

public sealed class AnonymousCancellationService(
    IRegistrationInventoryRepository inventory,
    IGuestRegistrationCapabilityRepository guestRegistrations,
    IEventRepository events,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    IAnonymousCancellationRepository repository,
    IRegistrationOrderTransitionCoordinator transitions,
    AdmissionRevocationService revocation,
    TimeProvider timeProvider)
{
    public async Task<AnonymousCancellationOutcome> ExecuteAsync(
        Guid eventId, Guid orderId, string? capabilityToken, bool cancel, CancellationToken cancellationToken)
    {
        if (!GuestRegistrationStatusAccessGuard.IsWellFormed(tenant.TenantId, eventId, orderId, capabilityToken))
            return AnonymousCancellationOutcome.InvalidAuthority;
        try
        {
            DateTime deadline = default;
            var result = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                var authorized = await GuestRegistrationStatusAccessGuard.GetAsync(
                    guestRegistrations, events, capabilities, tenant.TenantId, eventId, orderId,
                    capabilityToken!, timeProvider, token);
                if (authorized is not { } authority)
                    throw new CancellationRejectedException(AnonymousCancellationOutcome.InvalidAuthority);
                deadline = authority.Deadline;
                var context = await repository.LoadInCurrentTransactionAsync(tenant.TenantId, eventId, orderId, token);
                RequireLive(authority.Deadline);
                if (!cancel)
                    return context.IsEligible ? AnonymousCancellationOutcome.Eligible : AnonymousCancellationOutcome.Ineligible;
                if (context.IsCompleted)
                    return AnonymousCancellationOutcome.Cancelled;
                if (!context.IsEligible)
                    throw new CancellationRejectedException(AnonymousCancellationOutcome.Ineligible);

                // Retain pool fences before any mutation, then sample authority after the last
                // potentially blocking fence. Release only reacquires fences already owned here.
                await inventory.GetPoolsForUpdateAsync(context.Holds.Select(hold => hold.CapacityPoolId).ToArray(),
                    eventId, tenant.TenantId, token);
                RequireLive(authority.Deadline);
                DateTime cancelledAt = timeProvider.GetUtcNow().UtcDateTime;
                if (!await transitions.PersistAnonymousCancellationAsync(context.Order, context.Evidence, cancelledAt, token))
                    throw new InvalidOperationException("The locked anonymous cancellation transition was not accepted.");
                RequireLive(authority.Deadline);
                var revoked = await revocation.ReconcileInCurrentTransactionAsync(
                    new(tenant.TenantId, orderId, AdmissionRevocationService.OrderCancellationReason, []), token);
                if (revoked.Outcome != AdmissionRevocationOutcome.Applied ||
                    !revoked.RevokedTicketIds.Order().SequenceEqual(context.Tickets.Select(ticket => ticket.Id).Order()) ||
                    revoked.PreservedTicketIds.Count != 0)
                    throw new InvalidOperationException("Cancellation revocation must cover the exact locked ticket lineage.");
                RequireLive(authority.Deadline);
                var released = await repository.ReleaseConsumedInCurrentTransactionAsync(context, cancelledAt, token);
                if (!released.Order().SequenceEqual(context.Holds.Select(hold => hold.Id).Order()))
                    throw new InvalidOperationException("Cancellation release must cover the exact consumed inventory identities.");
                RequireLive(authority.Deadline);
                return AnonymousCancellationOutcome.Cancelled;
            }, cancellationToken);
            // Eligibility is a capability projection, so a commit/disposal wait must not disclose
            // a link after expiry. A committed cancellation still reports its successful outcome.
            return !cancel && deadline <= timeProvider.GetUtcNow().UtcDateTime
                ? AnonymousCancellationOutcome.InvalidAuthority : result;
        }
        catch (CancellationRejectedException exception) { return exception.Outcome; }
        catch (GuestRegistrationStatusAccessGuard.GuestStatusPromiseExpiredException)
        {
            return AnonymousCancellationOutcome.InvalidAuthority;
        }
    }

    private void RequireLive(DateTime deadline)
    {
        if (deadline <= timeProvider.GetUtcNow().UtcDateTime)
            throw new CancellationRejectedException(AnonymousCancellationOutcome.InvalidAuthority);
    }

    private sealed class CancellationRejectedException(AnonymousCancellationOutcome outcome) : Exception
    {
        public AnonymousCancellationOutcome Outcome { get; } = outcome;
    }
}
