namespace Explore.Domain;

/// <summary>Nonsecret current identity pointer owned by the Local credential lifecycle.</summary>
public sealed class LocalIdentityEligibility
{
    private LocalIdentityEligibility() { }

    public Guid Id { get; private set; }
    public Guid LocalSubjectId { get; private set; }
    public Guid CurrentOperationId { get; private set; }
    public Guid PersonalActorId { get; private set; }
    public Guid ExternalLoginId { get; private set; }
    public LocalIdentityEligibilityState State { get; private set; }
    public long Generation { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? LockoutUntilUtc { get; private set; }
    public bool IsEligible => State is LocalIdentityEligibilityState.ChangeRequired or LocalIdentityEligibilityState.Ready;

    public bool IsEligibleAt(DateTime observedAt)
    {
        RequireUtc(observedAt);
        return IsEligible && (LockoutUntilUtc is null || LockoutUntilUtc <= observedAt);
    }

    public void SetLockout(long expectedGeneration, Guid expectedOperationId, DateTime? lockoutUntilUtc,
        DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        if (State is LocalIdentityEligibilityState.Revoked or LocalIdentityEligibilityState.Deleted)
            throw new InvalidOperationException("An invalidated binding cannot change its lockout.");
        if (lockoutUntilUtc is { } deadline) RequireUtc(deadline);
        LockoutUntilUtc = lockoutUntilUtc;
        Advance(State, occurredAt);
    }

    public static LocalIdentityEligibility Begin(Guid localSubjectId, Guid currentOperationId,
        Guid personalActorId, Guid externalLoginId, DateTime occurredAt)
    {
        RequireId(localSubjectId, nameof(localSubjectId));
        RequireId(currentOperationId, nameof(currentOperationId));
        RequireId(personalActorId, nameof(personalActorId));
        RequireId(externalLoginId, nameof(externalLoginId));
        RequireUtc(occurredAt);
        return new LocalIdentityEligibility
        {
            Id = Guid.CreateVersion7(),
            LocalSubjectId = localSubjectId,
            CurrentOperationId = currentOperationId,
            PersonalActorId = personalActorId,
            ExternalLoginId = externalLoginId,
            State = LocalIdentityEligibilityState.ProvisioningPending,
            Generation = 1,
            CreatedAt = occurredAt
        };
    }

    public bool IsCurrent(Guid localSubjectId, Guid currentOperationId, Guid personalActorId,
        Guid externalLoginId, LocalIdentityEligibilityState state, long generation) =>
        IsEligible && LocalSubjectId == localSubjectId && CurrentOperationId == currentOperationId
        && PersonalActorId == personalActorId && ExternalLoginId == externalLoginId
        && State == state && Generation == generation;

    public void Activate(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        if (State != LocalIdentityEligibilityState.ProvisioningPending)
            throw new InvalidOperationException("Only the current pending binding can be activated.");
        Advance(LocalIdentityEligibilityState.ChangeRequired, occurredAt);
    }

    public void ReplaceCredential(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        if (State != LocalIdentityEligibilityState.ChangeRequired)
            throw new InvalidOperationException("Only the current change-required credential can be replaced.");
        Advance(LocalIdentityEligibilityState.Ready, occurredAt);
    }

    public void Reset(long expectedGeneration, Guid expectedOperationId, Guid newOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        RequireEligible();
        RequireId(newOperationId, nameof(newOperationId));
        if (newOperationId == CurrentOperationId)
            throw new ArgumentException("A reset must reserve a distinct current operation.", nameof(newOperationId));
        CurrentOperationId = newOperationId;
        Advance(LocalIdentityEligibilityState.ChangeRequired, occurredAt);
    }
    public void Rebind(long expectedGeneration, Guid expectedOperationId, Guid personalActorId,
        Guid externalLoginId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        RequireEligible();
        RequireId(personalActorId, nameof(personalActorId));
        RequireId(externalLoginId, nameof(externalLoginId));
        PersonalActorId = personalActorId;
        ExternalLoginId = externalLoginId;
        Advance(State, occurredAt);
    }

    public void Refresh(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        RequireEligible();
        Advance(State, occurredAt);
    }

    public void Revoke(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        if (State is LocalIdentityEligibilityState.Revoked or LocalIdentityEligibilityState.Deleted)
            throw new InvalidOperationException("An invalidated binding cannot be revoked again.");
        Advance(LocalIdentityEligibilityState.Revoked, occurredAt);
    }

    public void Delete(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, expectedOperationId, occurredAt);
        if (State == LocalIdentityEligibilityState.Deleted)
            throw new InvalidOperationException("A deleted binding cannot be deleted again.");
        Advance(LocalIdentityEligibilityState.Deleted, occurredAt);
    }

    private void RequireMutation(long expectedGeneration, Guid expectedOperationId, DateTime occurredAt)
    {
        if (Generation != expectedGeneration || CurrentOperationId != expectedOperationId)
            throw new InvalidOperationException("The current eligibility image has changed.");
        RequireUtc(occurredAt);
        if (occurredAt < (UpdatedAt ?? CreatedAt))
            throw new ArgumentException("Eligibility audit time cannot move backwards.", nameof(occurredAt));
        if (Generation == long.MaxValue)
            throw new InvalidOperationException("The eligibility generation is exhausted.");
    }

    private void RequireEligible()
    {
        if (!IsEligible) throw new InvalidOperationException("The current binding is not eligible.");
    }

    private void Advance(LocalIdentityEligibilityState state, DateTime occurredAt)
    {
        State = state;
        Generation++;
        UpdatedAt = occurredAt;
    }

    private static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty || value.Version != 7)
            throw new ArgumentException("Eligibility pointers require UUID version seven.", name);
    }

    private static void RequireUtc(DateTime occurredAt)
    {
        if (occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Eligibility audit time must be UTC.", nameof(occurredAt));
    }
}

public enum LocalIdentityEligibilityState
{
    ProvisioningPending = 1,
    ChangeRequired = 2,
    Ready = 3,
    Revoked = 4,
    Deleted = 5
}
