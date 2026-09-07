using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace ApplicationUnitTests.Contracts.Admissions.Support;

internal static class AdmissionIssuancePorts
{
    internal static AdmissionIssuanceService TypedService(AdmissionTestScenario scenario) => new(
        new IssuanceRepositoryFake(scenario),
        new IssuanceDigestFake(scenario),
        new IssuanceEnvelopeFake(),
        new IssuanceDeliveryFake(scenario),
        scenario.UnitOfWork,
        scenario.Clock);

    internal static AdmissionIssuanceRequest TypedRequest(AdmissionTestScenario scenario) => new(
        scenario.TenantId,
        scenario.OrderId,
        scenario.FinalizationEffectId,
        scenario.Authority);
}

internal sealed class IssuanceRepositoryFake(AdmissionTestScenario scenario) : IAdmissionIssuanceRepository
{
    public Task<AdmissionIssuanceContext?> LoadAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken) => Load(request);

    public Task<AdmissionIssuanceContext?> ReloadCommittedAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken) => Load(request);

    private Task<AdmissionIssuanceContext?> Load(AdmissionIssuanceRequest request)
    {
        AdmissionAssignmentFact[] assignments = scenario.AssignmentFacts.Select((fact, index) => new AdmissionAssignmentFact(
            fact.Line,
            fact.Assignment,
            fact.Participant,
            fact.TicketType,
            scenario.Assignments[index].LineUnitMinor,
            scenario.Assignments.Sum(value => value.LineUnitMinor),
            scenario.Assignments[index].IsAdmissionLine,
            new ParticipantAdmissionReadinessDecision(ParticipantAdmissionReadinessCode.Ready))).ToArray();
        AdmissionIssuanceContext context = new(
            scenario.TenantId,
            scenario.EventId,
            scenario.OrderId,
            scenario.FinalizationEffectId,
            scenario.Authority,
            scenario.PaymentReconciled,
            scenario.Authority != "PaymentSucceeded",
            scenario.Order,
            scenario.Catalog,
            assignments,
            scenario.TicketsByAssignment.Values.ToArray(),
            "attendee@example.test",
            scenario.DeliveryIntentsById.Values.ToArray());
        return Task.FromResult<AdmissionIssuanceContext?>(context);
    }

    public Task<AdmissionIssuanceResult> IssueAndScheduleDeliveryAsync(
        AdmissionIssuancePersistenceRequest request,
        CancellationToken cancellationToken)
    {
        var issued = new List<Guid>();
        var existing = new List<Guid>();
        foreach (AdmissionTicket ticket in request.Tickets)
        {
            Guid assignmentId = ticket.RegistrationTicketAssignmentId;
            if (scenario.TicketsByAssignment.TryAdd(assignmentId, ticket)) issued.Add(ticket.Id);
            else existing.Add(scenario.TicketsByAssignment[assignmentId].Id);
        }
        scenario.IssuanceWriteCalls++;
        scenario.PersistedDeliveryIntentCount += request.DeliveryIntents.Count;
        foreach (AdmissionDeliveryIntent intent in request.DeliveryIntents)
        {
            Guid intentId = intent.Id;
            if (!scenario.PendingDeliveryIntentIds.Add(intentId) || !scenario.DeliveryIntentsById.TryAdd(intentId, intent))
                throw new InvalidOperationException("Delivery intent identity must be unique and durable.");
        }
        scenario.AtomicTicketAndIntentWriteObserved |= scenario.UnitOfWork.InTransaction &&
                                                       request.Tickets.Count == request.DeliveryIntents.Count;
        return Task.FromResult(new AdmissionIssuanceResult(
            issued.Count == 0 ? AdmissionIssuanceOutcome.AlreadyIssued : AdmissionIssuanceOutcome.Issued,
            issued,
            existing,
            scenario.TicketsByAssignment.Values.ToArray(),
            request.DeliveryIntents.Select(intent => intent.Id).ToArray()));
    }
}

internal sealed class IssuanceDigestFake(AdmissionTestScenario scenario) : IAdmissionCredentialDigestService
{
    public Task<AdmissionCredentialMaterial> CreateAsync(
        AdmissionCredentialCreateRequest request,
        CancellationToken cancellationToken)
    {
        scenario.DigestIssueCalls++;
        string plaintext = RuntimeCapability.New();
        string digest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
        return Task.FromResult(new AdmissionCredentialMaterial(plaintext, digest, 7, 1));
    }

    public Task<AdmissionCredentialVerificationOutcome> VerifyAsync(
        AdmissionCredentialVerificationRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class IssuanceEnvelopeFake : IAdmissionDeliveryEnvelopeProtector
{
    public AdmissionProtectedDeliveryMaterial Protect(AdmissionCredentialDeliveryEnvelope envelope) => new(
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{envelope.RecipientAddress}\n{envelope.PlaintextCredential}")),
        1);

    public AdmissionCredentialDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        string[] values = Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext)).Split('\n', 2);
        return new AdmissionCredentialDeliveryEnvelope(values[0], values[1]);
    }
}

internal sealed class IssuanceDeliveryFake(AdmissionTestScenario scenario) : IAdmissionDeliveryDispatcher
{
    public Task<AdmissionDeliveryDispatchResult> DispatchAsync(
        AdmissionDeliveryDispatchRequest request,
        CancellationToken cancellationToken)
    {
        AdmissionDeliveryOutcome outcome = AdmissionDeliveryOutcome.Delivered;
        AdmissionDeliveryFailure failure = AdmissionDeliveryFailure.None;
        try
        {
            scenario.RecordIssuanceDispatch(request.DeliveryIntentId);
            AdmissionDeliveryIntent intent = scenario.DeliveryIntentsById[request.DeliveryIntentId];
            intent.MarkRouted(scenario.Clock.GetUtcNow().UtcDateTime);
            intent.CompleteHandoff($"test:{request.DeliveryIntentId:N}", scenario.Clock.GetUtcNow().UtcDateTime);
        }
        catch (AdmissionDeliveryUnavailableException)
        {
            outcome = AdmissionDeliveryOutcome.RecoverablePending;
            failure = AdmissionDeliveryFailure.RouteUnavailable;
        }
        return Task.FromResult(new AdmissionDeliveryDispatchResult(outcome, failure));
    }
}

internal static class RuntimeCapability
{
    internal static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
