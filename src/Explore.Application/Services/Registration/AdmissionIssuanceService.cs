using System.Data.Common;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionIssuanceService(
    IAdmissionIssuanceRepository repository,
    IAdmissionCredentialDigestService credentialDigestService,
    IAdmissionDeliveryEnvelopeProtector deliveryEnvelopeProtector,
    IAdmissionDeliveryDispatcher deliveryDispatcher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAdmissionIssuanceService
{
    private const string CredentialPurpose = "AdmissionTicket";
    private static readonly TimeSpan LocalIssuanceTimeout = TimeSpan.FromSeconds(30);

    public async Task<AdmissionIssuanceResult> IssueConfirmedAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.RegistrationOrderId == Guid.Empty ||
            request.FinalizationEffectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Authority))
        {
            return Empty(AdmissionIssuanceOutcome.InvalidRequest);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Empty(AdmissionIssuanceOutcome.CancelledBeforeCommit);
        }

        using var localTimeout = new CancellationTokenSource(LocalIssuanceTimeout);
        PreparedIssuance? staged = null;
        try
        {
            staged = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                PreparedIssuance prepared = await PrepareWithinFenceAsync(request, token);
                staged = prepared;
                return prepared;
            }, localTimeout.Token);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or TimeoutException or DbException)
        {
            AdmissionIssuanceContext? committed = await repository.ReloadCommittedAsync(
                request, CancellationToken.None);
            if (!HasCompleteIssuance(committed))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(exception)
                    .Throw();
            }

            staged = FromCommittedReload(committed!, staged?.OneTimeCredentials ?? []);
        }

        if (staged.Context is null)
        {
            return staged.Result;
        }

        AdmissionDeliveryDispatchResult delivery = await DispatchIncompleteAsync(
            staged.Context.ExistingDeliveryIntents ?? [], CancellationToken.None);
        return new AdmissionIssuanceResult(
            staged.Result.Outcome,
            staged.Result.IssuedTicketIds,
            staged.Result.ExistingTicketIds,
            staged.Result.Tickets,
            staged.Result.DeliveryIntentIds,
            staged.OneTimeCredentials,
            delivery.Outcome,
            delivery.Failure);
    }

    private async Task<PreparedIssuance> PrepareWithinFenceAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken)
    {
        AdmissionIssuanceContext? context = await repository.LoadAsync(request, cancellationToken);
        if (!IsConfirmedAuthority(context, request))
        {
            return new PreparedIssuance(Empty(AdmissionIssuanceOutcome.NotConfirmed), [], context);
        }

        AdmissionAssignmentFact[] assignments = context!.Assignments
            .Where(assignment =>
                assignment.IsAdmissionLine
                && assignment.Readiness?.IsReady == true)
            .ToArray();
        if (assignments.Length == 0)
        {
            AdmissionIssuanceOutcome outcome =
                context.Assignments.Any(
                    assignment => assignment.IsAdmissionLine)
                    ? AdmissionIssuanceOutcome.ReadinessPending
                    : AdmissionIssuanceOutcome.NoAssignments;
            return new PreparedIssuance(
                Empty(outcome),
                [],
                context);
        }

        AdmissionOneTimeCredential[] recoverable = RestoreIncompleteCredentials(context).ToArray();
        if (context.ExistingTickets.Count == assignments.Length)
        {
            Guid[] ticketIds = context.ExistingTickets.Select(ticket => ticket.Id).ToArray();
            Guid[] intentIds = (context.ExistingDeliveryIntents ?? []).Select(intent => intent.Id).ToArray();
            return new PreparedIssuance(
                new AdmissionIssuanceResult(
                    AdmissionIssuanceOutcome.AlreadyIssued,
                    [],
                    ticketIds,
                    context.ExistingTickets,
                    intentIds),
                recoverable,
                context);
        }

        HashSet<Guid> existingAssignments = context.ExistingTickets
            .Select(ticket => ticket.RegistrationTicketAssignmentId)
            .ToHashSet();
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var tickets = new List<AdmissionTicket>();
        var intents = new List<AdmissionDeliveryIntent>();
        var oneTimeCredentials = new List<AdmissionOneTimeCredential>(recoverable);

        foreach (AdmissionAssignmentFact fact in assignments
                     .Where(fact => !existingAssignments.Contains(fact.RegistrationTicketAssignmentId)))
        {
            Guid ticketId = StableId(context.TenantId, context.FinalizationEffectId,
                fact.RegistrationTicketAssignmentId, "ticket");
            Guid credentialId = StableId(context.TenantId, context.FinalizationEffectId,
                fact.RegistrationTicketAssignmentId, "credential");
            Guid deliveryIntentId = StableId(context.TenantId, context.FinalizationEffectId,
                fact.RegistrationTicketAssignmentId, "delivery");
            AdmissionCredentialMaterial material = await credentialDigestService.CreateAsync(
                new AdmissionCredentialCreateRequest(
                    context.TenantId, ticketId, credentialId, CredentialPurpose, 1),
                cancellationToken);
            AdmissionProtectedDeliveryMaterial protectedMaterial = deliveryEnvelopeProtector.Protect(
                new AdmissionCredentialDeliveryEnvelope(context.DeliveryAddress, material.PlaintextCredential));
            AdmissionTicket ticket = AdmissionTicket.Issue(
                context.Order,
                fact.OrderLine,
                fact.Assignment,
                fact.Participant,
                context.TicketCatalogVersion,
                fact.EventTicketType,
                ticketId,
                ticketId.ToString("N"),
                credentialId,
                material.CredentialVersion,
                material.KeyVersion,
                material.LookupDigest,
                nowUtc);

            tickets.Add(ticket);
            intents.Add(new AdmissionDeliveryIntent(
                deliveryIntentId,
                context.TenantId,
                context.FinalizationEffectId,
                fact.RegistrationTicketAssignmentId,
                ticketId,
                protectedMaterial.Ciphertext,
                protectedMaterial.ProtectionVersion,
                nowUtc));
            oneTimeCredentials.Add(new AdmissionOneTimeCredential(ticketId, material.PlaintextCredential));
        }

        AdmissionIssuanceResult persisted = await repository.IssueAndScheduleDeliveryAsync(
            new AdmissionIssuancePersistenceRequest(
                context.TenantId,
                context.RegistrationOrderId,
                context.FinalizationEffectId,
                tickets,
                intents),
            cancellationToken);
        AdmissionIssuanceContext committedContext = context with
        {
            ExistingTickets = persisted.Tickets,
            ExistingDeliveryIntents = (context.ExistingDeliveryIntents ?? []).Concat(intents).ToArray()
        };
        return new PreparedIssuance(persisted, oneTimeCredentials, committedContext);
    }

    private PreparedIssuance FromCommittedReload(
        AdmissionIssuanceContext context,
        IReadOnlyList<AdmissionOneTimeCredential> stagedCredentials)
    {
        IReadOnlyList<AdmissionOneTimeCredential> credentials = stagedCredentials.Count > 0
            ? stagedCredentials
            : RestoreIncompleteCredentials(context);
        Guid[] ticketIds = context.ExistingTickets.Select(ticket => ticket.Id).ToArray();
        Guid[] intentIds = (context.ExistingDeliveryIntents ?? []).Select(intent => intent.Id).ToArray();
        return new PreparedIssuance(
            new AdmissionIssuanceResult(
                AdmissionIssuanceOutcome.AlreadyIssued,
                [],
                ticketIds,
                context.ExistingTickets,
                intentIds),
            credentials,
            context);
    }

    private IReadOnlyList<AdmissionOneTimeCredential> RestoreIncompleteCredentials(AdmissionIssuanceContext context)
    {
        var restored = new List<AdmissionOneTimeCredential>();
        foreach (AdmissionDeliveryIntent intent in context.ExistingDeliveryIntents ?? [])
        {
            if (intent.HandoffCompletedAt is not null || string.IsNullOrWhiteSpace(intent.ProtectedCredential))
            {
                continue;
            }

            try
            {
                AdmissionCredentialDeliveryEnvelope envelope = deliveryEnvelopeProtector.Unprotect(
                    intent.ProtectedCredential, intent.ProtectionVersion);
                restored.Add(new AdmissionOneTimeCredential(intent.AdmissionTicketId, envelope.PlaintextCredential));
            }
            catch (InvalidOperationException)
            {
            }
        }

        return restored;
    }

    private async Task<AdmissionDeliveryDispatchResult> DispatchIncompleteAsync(
        IReadOnlyList<AdmissionDeliveryIntent> intents,
        CancellationToken cancellationToken)
    {
        AdmissionDeliveryIntent[] incomplete = intents
            .Where(intent => intent.HandoffCompletedAt is null)
            .ToArray();
        if (incomplete.Length == 0)
        {
            return new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered);
        }

        AdmissionDeliveryDispatchResult aggregate = new(AdmissionDeliveryOutcome.Delivered);
        foreach (AdmissionDeliveryIntent intent in incomplete)
        {
            AdmissionDeliveryDispatchResult result;
            try
            {
                result = await deliveryDispatcher.DispatchAsync(
                    new AdmissionDeliveryDispatchRequest(intent.Id), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result = new AdmissionDeliveryDispatchResult(
                    AdmissionDeliveryOutcome.RecoverablePending,
                    AdmissionDeliveryFailure.Cancelled);
            }
            catch (Exception)
            {
                result = new AdmissionDeliveryDispatchResult(
                    AdmissionDeliveryOutcome.RecoverablePending,
                    AdmissionDeliveryFailure.RouteUnavailable);
            }
            if (result.Outcome != AdmissionDeliveryOutcome.Delivered)
            {
                aggregate = result;
            }
        }

        return aggregate;
    }

    private static bool HasCompleteIssuance(AdmissionIssuanceContext? context) =>
        context is not null && context.Assignments.Count(assignment => assignment.IsAdmissionLine) > 0 &&
        context.ExistingTickets.Count == context.Assignments.Count(assignment => assignment.IsAdmissionLine);

    private static AdmissionIssuanceResult Empty(AdmissionIssuanceOutcome outcome) =>
        new(outcome, [], [], [], []);

    private static bool IsConfirmedAuthority(
        AdmissionIssuanceContext? context,
        AdmissionIssuanceRequest request)
    {
        if (context is null ||
            context.TenantId != request.TenantId ||
            context.RegistrationOrderId != request.RegistrationOrderId ||
            context.FinalizationEffectId != request.FinalizationEffectId ||
            !context.OrderConfirmed ||
            string.IsNullOrWhiteSpace(context.DeliveryAddress))
        {
            return false;
        }

        return request.Authority == context.Authority &&
            (request.Authority == AdmissionIssuanceAuthority.ConfirmedFreeOrder &&
             !context.PaymentReconciled &&
             context.Order.TotalDueMinorSnapshot == 0 ||
             request.Authority == AdmissionIssuanceAuthority.ReconciledPaidFinalization &&
             context.PaymentReconciled &&
             context.Order.TotalDueMinorSnapshot > 0);
    }

    private static Guid StableId(Guid tenantId, Guid effectId, Guid assignmentId, string purpose) =>
        AdmissionIssuanceIdentityFactory.Create(tenantId, effectId, assignmentId, purpose);

    private sealed record PreparedIssuance(
        AdmissionIssuanceResult Result,
        IReadOnlyList<AdmissionOneTimeCredential> OneTimeCredentials,
        AdmissionIssuanceContext? Context);
}
