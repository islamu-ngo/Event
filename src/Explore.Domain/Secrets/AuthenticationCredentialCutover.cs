using System.Collections.ObjectModel;

namespace Explore.Domain.Secrets;

/// <summary>
/// Exact, instance-owned Google/Keycloak coordinated-restart evidence. The persistence owner must
/// serialize mutations, compare the current operation and stamp, and maintain the attested fences
/// through the cleanup transaction. This operation neither acquires nor deletes credentials.
/// </summary>
public sealed class AuthenticationCredentialCutover
{
    public enum CutoverState { Prepared, Collecting, ReadyForCleanup, Cleaned, Failed, Expired, Superseded }
    public enum CredentialAuthority { Environment, Infisical, UserSecrets }
    public enum CredentialConsumer { GoogleOidc, KeycloakOidc }

    /// <summary>
    /// Server-approved replacement process and consumer. Generation is an opaque random identity,
    /// never a value digest; previous boot identities prevent reusing an old process as a replacement.
    /// </summary>
    public sealed record Replacement(Guid ReplicaId, Guid PreviousProcessBootId, Guid ProcessBootId,
        CredentialConsumer Consumer, Guid Generation);

    /// <summary>Value-free actual-acquisition evidence; no provider coordinates or credential material.</summary>
    public sealed record ActivationReport(Guid InstanceId, Guid OperationId, Guid DeploymentRevision,
        Guid ReplicaId, Guid ProcessBootId, CredentialConsumer Consumer, Guid Generation);

    /// <summary>
    /// Operator assertion covering every old process and writer/session for this exact deployment.
    /// Authentication and verification of the operator's assertion belong to the application owner.
    /// </summary>
    public sealed record FenceAttestation(Guid InstanceId, Guid OperationId, Guid DeploymentRevision,
        Guid AttestedBy, bool OldProcessesTerminated, bool OldWritersFenced, bool OldSessionsFenced);

    private AuthenticationCredentialCutover()
    {
        RequiredReplacements = Array.AsReadOnly(Array.Empty<Replacement>());
        Activations = Array.AsReadOnly(Array.Empty<ActivationReport>());
    }

    private AuthenticationCredentialCutover(Replacement[] replacements) : this()
    {
        RequiredReplacements = Array.AsReadOnly(replacements);
    }

    public Guid Id { get; private init; }
    public Guid InstanceId { get; private init; }
    public Guid CreatedBy { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset Deadline { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid DeploymentRevision { get; private init; }
    public CredentialAuthority Authority { get; private init; }
    public Guid AuthorityProfileRevision { get; private init; }
    public Guid ConcurrencyStamp { get; private set; }
    public CutoverState State { get; private set; }
    public ReadOnlyCollection<Replacement> RequiredReplacements { get; private set; }
    public ReadOnlyCollection<ActivationReport> Activations { get; private set; }
    public int RequiredActivationCount => RequiredReplacements.Count;
    public int ActivationCount => Activations.Count;
    public FenceAttestation? Fence { get; private set; }
    public int? CleanedSettingCount { get; private set; }
    public int? CleanedBindingCount { get; private set; }

    /// <summary>
    /// Inputs come from the server's approved deployment inventory, not callback discovery.
    /// UserSecrets environment eligibility must be checked by the composition owner.
    /// </summary>
    public static AuthenticationCredentialCutover Create(Guid instanceId, Guid operationId, Guid actorId,
        DateTimeOffset createdAt, DateTimeOffset deadline, Guid deploymentRevision, CredentialAuthority authority,
        Guid authorityProfileRevision, IEnumerable<Replacement> requiredReplacements)
    {
        ArgumentNullException.ThrowIfNull(requiredReplacements);
        if (instanceId == Guid.Empty || operationId == Guid.Empty || actorId == Guid.Empty
            || deploymentRevision == Guid.Empty || authorityProfileRevision == Guid.Empty
            || !Enum.IsDefined(authority) || createdAt == default || deadline <= createdAt)
            throw new ArgumentException("authentication_cutover_invalid_preparation");

        // Take bounded ownership before validating. Callers cannot change approved membership later.
        var replacements = requiredReplacements.Take(1025).ToArray();
        if (replacements.Length is 0 or > 1024
            || replacements.Any(member => member is null || member.ReplicaId == Guid.Empty
                || member.PreviousProcessBootId == Guid.Empty || member.ProcessBootId == Guid.Empty
                || member.Generation == Guid.Empty || !Enum.IsDefined(member.Consumer))
            || replacements.Select(member => (member.ReplicaId, member.Consumer)).Distinct().Count() != replacements.Length
            || replacements.Any(member => replacements.Any(previous =>
                previous.PreviousProcessBootId == member.ProcessBootId))
            || replacements.GroupBy(member => member.ReplicaId).Any(replica =>
                replica.Select(member => (member.PreviousProcessBootId, member.ProcessBootId)).Distinct().Count() != 1)
            || replacements.GroupBy(member => member.ProcessBootId).Any(boot =>
                boot.Select(member => member.ReplicaId).Distinct().Count() != 1))
            throw new ArgumentException("authentication_cutover_invalid_membership");

        return new(replacements)
        {
            Id = operationId,
            InstanceId = instanceId,
            CreatedBy = actorId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Deadline = deadline,
            DeploymentRevision = deploymentRevision,
            Authority = authority,
            AuthorityProfileRevision = authorityProfileRevision,
            ConcurrencyStamp = Guid.CreateVersion7(),
            State = CutoverState.Prepared
        };
    }

    public void StartCollecting(Guid expectedStamp, DateTimeOffset now)
    {
        EnsureActive(expectedStamp, now);
        if (State != CutoverState.Prepared)
            throw new InvalidOperationException("authentication_cutover_not_prepared");
        State = CutoverState.Collecting;
        Touch(now);
    }

    public void ReportActivation(ActivationReport report, Guid expectedStamp, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        EnsureCollecting(expectedStamp, now);
        if (report.InstanceId != InstanceId || report.OperationId != Id
            || report.DeploymentRevision != DeploymentRevision)
            throw new InvalidOperationException("authentication_cutover_wrong_target");

        var previous = Activations.FirstOrDefault(evidence =>
            evidence.ReplicaId == report.ReplicaId && evidence.Consumer == report.Consumer);
        if (previous is not null)
        {
            if (previous != report)
                throw new InvalidOperationException("authentication_cutover_conflicting_activation");
            return;
        }

        if (!RequiredReplacements.Any(member => member.ReplicaId == report.ReplicaId
            && member.Consumer == report.Consumer && member.ProcessBootId == report.ProcessBootId
            && member.Generation == report.Generation))
            throw new InvalidOperationException("authentication_cutover_unexpected_activation");

        Activations = Array.AsReadOnly(Activations.Append(report).ToArray());
        RefreshReadiness();
        Touch(now);
    }

    public void AttestFence(FenceAttestation proof, Guid expectedStamp, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(proof);
        EnsureCollecting(expectedStamp, now);
        if (Fence is not null)
        {
            if (Fence != proof)
                throw new InvalidOperationException("authentication_cutover_conflicting_fence");
            return;
        }
        if (proof.InstanceId != InstanceId || proof.OperationId != Id
            || proof.DeploymentRevision != DeploymentRevision || proof.AttestedBy != CreatedBy
            || !proof.OldProcessesTerminated || !proof.OldWritersFenced || !proof.OldSessionsFenced)
            throw new InvalidOperationException("authentication_cutover_incomplete_fence");
        Fence = proof;
        RefreshReadiness();
        Touch(now);
    }

    /// <summary>
    /// Recheck inside the owner's atomic cleanup transaction against its current-operation pointer
    /// and stored optimistic stamp. Expiry is a terminal mutation that the owner must persist.
    /// </summary>
    public void EnsureCleanupAllowed(Guid currentOperationId, Guid expectedStamp, DateTimeOffset now)
    {
        EnsureActive(expectedStamp, now);
        if (currentOperationId != Id)
            throw new InvalidOperationException("authentication_cutover_not_current");
        if (State != CutoverState.ReadyForCleanup || Fence is null || ActivationCount != RequiredActivationCount)
            throw new InvalidOperationException("authentication_cutover_not_ready");
    }

    /// <summary>
    /// Record only row counts in the same transaction as exact cleanup; never call remote providers
    /// in that transaction. This method is not evidence that database cleanup has actually happened.
    /// </summary>
    public void CompleteCleanup(Guid currentOperationId, Guid expectedStamp, int settingCount, int bindingCount,
        DateTimeOffset now)
    {
        EnsureCleanupAllowed(currentOperationId, expectedStamp, now);
        if (settingCount < 0 || bindingCount < 0)
            throw new ArgumentException("authentication_cutover_invalid_counts");
        CleanedSettingCount = settingCount;
        CleanedBindingCount = bindingCount;
        State = CutoverState.Cleaned;
        Touch(now);
    }

    public void Fail(Guid expectedStamp, DateTimeOffset now)
    {
        EnsureActive(expectedStamp, now);
        State = CutoverState.Failed;
        Touch(now);
    }

    public void Supersede(Guid expectedStamp, DateTimeOffset now)
    {
        EnsureActive(expectedStamp, now);
        State = CutoverState.Superseded;
        Touch(now);
    }

    private void EnsureCollecting(Guid expectedStamp, DateTimeOffset now)
    {
        EnsureActive(expectedStamp, now);
        if (State is not (CutoverState.Collecting or CutoverState.ReadyForCleanup))
            throw new InvalidOperationException("authentication_cutover_not_collecting");
    }

    private void EnsureActive(Guid expectedStamp, DateTimeOffset now)
    {
        // Terminal state precedes expiry: late callbacks cannot rewrite a completed outcome.
        if (State is CutoverState.Cleaned or CutoverState.Failed or CutoverState.Expired or CutoverState.Superseded)
            throw new InvalidOperationException("authentication_cutover_terminal");
        if (expectedStamp != ConcurrencyStamp)
            throw new InvalidOperationException("authentication_cutover_stale_stamp");
        if (now < UpdatedAt)
            throw new InvalidOperationException("authentication_cutover_late_callback");
        if (now >= Deadline)
        {
            State = CutoverState.Expired;
            Touch(now);
            throw new InvalidOperationException("authentication_cutover_expired");
        }
    }

    private void RefreshReadiness()
    {
        if (Fence is not null && ActivationCount == RequiredActivationCount)
            State = CutoverState.ReadyForCleanup;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public override string ToString() => nameof(AuthenticationCredentialCutover);
}
