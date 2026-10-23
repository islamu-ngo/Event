namespace Explore.Domain.Secrets;

/// <summary>
/// Instance-owned current operation. Persistence must flush this head's conditional stamp update
/// inside the same transaction before inspecting mutable evidence or cleaning credential rows.
/// </summary>
public sealed class AuthenticationCredentialCutoverHead
{
    private AuthenticationCredentialCutoverHead() { }

    public Guid InstanceId { get; private init; }
    public Guid CurrentOperationId { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public static AuthenticationCredentialCutoverHead Create(AuthenticationCredentialCutover operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.State != AuthenticationCredentialCutover.CutoverState.Prepared)
            throw new InvalidOperationException("authentication_cutover_head_invalid_preparation");
        return new()
        {
            InstanceId = operation.InstanceId,
            CurrentOperationId = operation.Id,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void Guard(Guid expectedOperationId, Guid expectedStamp)
    {
        if (expectedOperationId != CurrentOperationId || expectedStamp != ConcurrencyStamp)
            throw new InvalidOperationException("authentication_cutover_head_conflict");
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    /// <summary>
    /// The repository must insert the fresh replacement receipt in the guarded transaction;
    /// existing operation IDs are never reused or moved back into the head.
    /// </summary>
    public void Replace(AuthenticationCredentialCutover replacement, Guid expectedOperationId, Guid expectedStamp)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.InstanceId != InstanceId || replacement.Id == CurrentOperationId
            || replacement.State != AuthenticationCredentialCutover.CutoverState.Prepared)
            throw new InvalidOperationException("authentication_cutover_head_invalid_replacement");
        Guard(expectedOperationId, expectedStamp);
        CurrentOperationId = replacement.Id;
    }

    public override string ToString() => nameof(AuthenticationCredentialCutoverHead);
}
