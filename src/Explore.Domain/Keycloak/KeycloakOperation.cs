namespace Explore.Domain.Keycloak;

public enum KeycloakOperationState
{
    Previewed,
    Applying,
    Verified,
    PartiallyApplied,
    OutcomeUnknown,
    Conflict,
    FailedBeforeWrite,
    Cancelled,
    Expired
}

public sealed class KeycloakOperation
{
    private KeycloakOperation()
    {
    }

    public KeycloakOperation(
        KeycloakChangeSet changeSet,
        KeycloakTarget target,
        string actor,
        long setupGeneration,
        string digest,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Actor = Required(actor, nameof(actor));
        Digest = Required(digest, nameof(digest));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(setupGeneration);

        SetupGeneration = setupGeneration;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        if (ExpiresAtUtc <= CreatedAtUtc)
        {
            throw new ArgumentException(
                "Expiry must be later than creation.",
                nameof(expiresAtUtc));
        }
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public KeycloakChangeSet ChangeSet { get; } = null!;

    public KeycloakTarget Target { get; } = null!;

    public string Actor { get; } = string.Empty;

    public long SetupGeneration { get; }

    public string Digest { get; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? SettledAtUtc { get; private set; }

    public long? SettledAtUtcTicks { get; private set; }

    public Guid ConcurrencyStamp { get; private set; } = Guid.CreateVersion7();

    public KeycloakOperationState State { get; private set; } =
        KeycloakOperationState.Previewed;

    public bool IsCancellationRequested { get; private set; }

    public KeycloakStepOutcomeSet StepOutcomes { get; private set; } = new();

    public void AuthorizeApply(
        string actor,
        long setupGeneration,
        KeycloakTarget target,
        string digest,
        DateTimeOffset nowUtc)
    {
        EnsureBinding(actor, setupGeneration, target, digest);
        EnsureState(KeycloakOperationState.Previewed);

        DateTimeOffset now = nowUtc.ToUniversalTime();
        if (now < CreatedAtUtc)
        {
            throw new InvalidOperationException(
                "The operation cannot be authorized before it was created.");
        }

        if (now >= ExpiresAtUtc)
        {
            State = KeycloakOperationState.Expired;
            Settle(now);
            throw new InvalidOperationException("The operation approval has expired.");
        }

        State = KeycloakOperationState.Applying;
        Touch();
    }

    public void MarkVerified(DateTimeOffset settledAtUtc) =>
        SettleApplying(KeycloakOperationState.Verified, settledAtUtc);

    public void MarkPartiallyApplied(DateTimeOffset settledAtUtc) =>
        SettleApplying(KeycloakOperationState.PartiallyApplied, settledAtUtc);

    public void MarkConflict(DateTimeOffset settledAtUtc) =>
        SettleApplying(KeycloakOperationState.Conflict, settledAtUtc);

    public void MarkFailedBeforeWrite(DateTimeOffset settledAtUtc) =>
        SettleApplying(KeycloakOperationState.FailedBeforeWrite, settledAtUtc);

    public void MarkOutcomeUnknown()
    {
        EnsureState(KeycloakOperationState.Applying);
        if (!StepOutcomes.Items.Any(outcome =>
                outcome.Kind == KeycloakStepOutcomeKind.OutcomeUnknown))
        {
            throw new InvalidOperationException(
                "An uncertain operation must identify the uncertain step.");
        }

        State = KeycloakOperationState.OutcomeUnknown;
        Touch();
    }

    public void RecordStepOutcome(KeycloakStepOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (State is not (
                KeycloakOperationState.Applying
                or KeycloakOperationState.OutcomeUnknown)
            || !ChangeSet.Steps.Any(step =>
                string.Equals(
                    step.StepId,
                    outcome.StepId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The outcome does not belong to an active approved step.");
        }

        StepOutcomes = StepOutcomes.Add(outcome);
        Touch();
    }

    public void ReconcileStepOutcome(KeycloakStepOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureState(KeycloakOperationState.OutcomeUnknown);
        if (!ChangeSet.Steps.Any(step =>
                string.Equals(
                    step.StepId,
                    outcome.StepId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The read-back outcome does not belong to an approved step.");
        }

        StepOutcomes = StepOutcomes.Reconcile(outcome);
        Touch();
    }

    public void RequestCancellation(DateTimeOffset requestedAtUtc)
    {
        if (State == KeycloakOperationState.Previewed)
        {
            DateTimeOffset settled = ValidSettlementTime(requestedAtUtc);
            IsCancellationRequested = true;
            State = KeycloakOperationState.Cancelled;
            SettleAt(settled);
            return;
        }

        if (State == KeycloakOperationState.Applying)
        {
            IsCancellationRequested = true;
            Touch();
            return;
        }

        throw new InvalidOperationException(
            "Cancellation cannot change a settled or uncertain operation.");
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        if (State == KeycloakOperationState.Previewed && now >= ExpiresAtUtc)
        {
            State = KeycloakOperationState.Expired;
            Settle(now);
        }
    }

    public void Reconcile(
        KeycloakOperationState readBackState,
        DateTimeOffset settledAtUtc)
    {
        if (State is not (
                KeycloakOperationState.Applying
                or KeycloakOperationState.OutcomeUnknown)
            || readBackState is not (
                KeycloakOperationState.Verified
                or KeycloakOperationState.PartiallyApplied
                or KeycloakOperationState.Conflict
                or KeycloakOperationState.FailedBeforeWrite))
        {
            throw new InvalidOperationException(
                "Only provider read-back may settle an applying or uncertain operation.");
        }

        KeycloakOperationState derivedState = DeriveSettledState();
        if (readBackState != derivedState)
        {
            throw new InvalidOperationException(
                $"Step outcomes require aggregate state {derivedState}.");
        }

        DateTimeOffset settled = ValidSettlementTime(settledAtUtc);
        State = readBackState;
        SettleAt(settled);
    }

    private void SettleApplying(
        KeycloakOperationState state,
        DateTimeOffset settledAtUtc)
    {
        DateTimeOffset settled = ValidSettlementTime(settledAtUtc);
        EnsureState(KeycloakOperationState.Applying);
        KeycloakOperationState derivedState = DeriveSettledState();
        if (state != derivedState)
        {
            throw new InvalidOperationException(
                $"Step outcomes require aggregate state {derivedState}.");
        }

        State = state;
        SettleAt(settled);
    }

    private KeycloakOperationState DeriveSettledState()
    {
        if (StepOutcomes.Items.Count != ChangeSet.Steps.Count
            || ChangeSet.Steps.Any(step => !StepOutcomes.Items.Any(outcome =>
                string.Equals(
                    outcome.StepId,
                    step.StepId,
                    StringComparison.Ordinal)))
            || StepOutcomes.Items.Any(outcome =>
                outcome.Kind == KeycloakStepOutcomeKind.OutcomeUnknown))
        {
            throw new InvalidOperationException(
                "Every approved step requires a settled recovery outcome.");
        }

        bool anySuccess = StepOutcomes.Items.Any(outcome =>
            outcome.Kind is KeycloakStepOutcomeKind.Applied
                or KeycloakStepOutcomeKind.Verified
                or KeycloakStepOutcomeKind.NoChange);
        bool anyConflict = StepOutcomes.Items.Any(outcome =>
            outcome.Kind == KeycloakStepOutcomeKind.Conflict);
        bool anyFailure = StepOutcomes.Items.Any(outcome =>
            outcome.Kind is KeycloakStepOutcomeKind.FailedBeforeWrite
                or KeycloakStepOutcomeKind.SkippedCancelled);

        if (anySuccess && !anyConflict && !anyFailure)
        {
            return KeycloakOperationState.Verified;
        }

        if (anySuccess)
        {
            return KeycloakOperationState.PartiallyApplied;
        }

        return anyConflict
            ? KeycloakOperationState.Conflict
            : KeycloakOperationState.FailedBeforeWrite;
    }

    private void EnsureBinding(
        string actor,
        long setupGeneration,
        KeycloakTarget target,
        string digest)
    {
        if (!string.Equals(Actor, actor?.Trim(), StringComparison.Ordinal)
            || SetupGeneration != setupGeneration
            || Target != target
            || !string.Equals(Digest, digest?.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The approval binding no longer matches the operation.");
        }
    }

    private void EnsureState(KeycloakOperationState requiredState)
    {
        if (State != requiredState)
        {
            throw new InvalidOperationException(
                $"Operation state {State} cannot perform this transition.");
        }
    }

    private void Settle(DateTimeOffset settledAtUtc)
    {
        SettleAt(ValidSettlementTime(settledAtUtc));
    }

    private DateTimeOffset ValidSettlementTime(DateTimeOffset settledAtUtc)
    {
        DateTimeOffset settled = settledAtUtc.ToUniversalTime();
        if (settled < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settledAtUtc),
                "Settlement cannot precede operation creation.");
        }

        return settled;
    }

    private void SettleAt(DateTimeOffset settled)
    {
        SettledAtUtc = settled;
        SettledAtUtcTicks = SettledAtUtc.Value.UtcTicks;
        Touch();
    }

    private void Touch() => ConcurrencyStamp = Guid.CreateVersion7();

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }
}
