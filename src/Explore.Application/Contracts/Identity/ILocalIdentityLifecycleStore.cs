// ABOUTME: Defines transport-neutral, immutable Local lifecycle operation authority.
// ABOUTME: Durable pointers and mirror receipts contain no token, password intent, or session authority.

using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Contracts.Identity;

public enum LocalIdentityLifecyclePurpose { EmailVerification = 1, EmailChange = 2, PasswordRecovery = 3 }
public enum LocalIdentityLifecycleOutcome { Consumed = 1, Invalid = 2, InvalidPassword = 3, SamePassword = 4, Conflict = 5 }

public interface ILocalIdentityLifecycleStore
{
    Task<LocalIdentityLifecycleRequest?> FindRequestByIdentifierAsync(string identifier, LocalIdentityLifecyclePurpose purpose, CancellationToken cancellationToken);
    Task<LocalIdentityLifecycleOutcome> ChangePasswordAsync(LocalIdentityPasswordChangeRequest request, CancellationToken cancellationToken);
    // Token-authorized consumed receipt retry. The store owns native + Application serializable transactions;
    // the callback performs exact-bound idempotent mirror writes WITHOUT opening or committing a transaction.
    // Existing native credential mutations serialize on the same Identity user row. External mirror commit and
    // native acknowledgement are separate: an acknowledgement loss may repeat the idempotent callback.
    Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecycleConsumption authority,
        Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken);
    // Trusted durable reconciliation ONLY, including after transport-token expiry. Never expose this overload
    // as public retry authority. The exact consumed receipt/resulting native stamp must still be current.
    Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecyclePointer operation,
        Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken);
    // Trusted Application callers resolve exact Local binding before intake. Email change additionally requires
    // the current authenticated security stamp. Anonymous intake must hide null/account-specific outcomes.
    Task<LocalIdentityLifecyclePointer?> BeginAsync(LocalIdentityLifecycleRequest request, CancellationToken cancellationToken);
    // Only an admitted transport handoff may call this. Never persist or log the returned token.
    Task<LocalIdentityLifecycleTransport?> IssueTransportTokenAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken);
    Task<LocalIdentityLifecycleResult> ConsumeAsync(LocalIdentityLifecycleConsumption request, CancellationToken cancellationToken);
    // Retry mirror synchronization independently of consumption; never replay password mutation or log in.
    // A receipt is available only while its resulting Identity stamp and exact application binding remain current.
    // Inspection only; public retry MUST use ExecuteSynchronizationAsync with the original token, never this
    // pointer-only method. Null requires fresh reconciliation, not application of an older email/profile snapshot.
    // No cross-database atomicity is claimed.
    Task<LocalIdentityLifecycleSynchronization?> ReadSynchronizationAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken);
    Task<bool> MarkSynchronizedAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken);
}

public sealed record LocalIdentityPasswordChangeRequest
{
    public LocalIdentityPasswordChangeRequest(LocalSessionAuthority authority, Guid personalActorId, Guid externalLoginId,
        string currentPassword, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (personalActorId == Guid.Empty || externalLoginId == Guid.Empty) throw new ArgumentException("An exact Local binding is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword); ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        Authority = authority; PersonalActorId = personalActorId; ExternalLoginId = externalLoginId;
        CurrentPassword = currentPassword; NewPassword = newPassword;
    }
    public LocalSessionAuthority Authority { get; }
    public Guid PersonalActorId { get; }
    public Guid ExternalLoginId { get; }
    public string CurrentPassword { get; }
    public string NewPassword { get; }
    public override string ToString() => nameof(LocalIdentityPasswordChangeRequest);
}

public sealed record LocalIdentityLifecyclePointer
{
    public LocalIdentityLifecyclePointer(Guid operationId, Guid localSubjectId, Guid personalActorId,
        Guid externalLoginId, LocalIdentityLifecyclePurpose purpose, Guid generation)
    {
        if (operationId == Guid.Empty || localSubjectId == Guid.Empty || personalActorId == Guid.Empty
            || externalLoginId == Guid.Empty || generation == Guid.Empty || !Enum.IsDefined(purpose))
            throw new ArgumentException("A complete Local operation binding is required.");
        OperationId = operationId; LocalSubjectId = localSubjectId; PersonalActorId = personalActorId;
        ExternalLoginId = externalLoginId; Purpose = purpose; Generation = generation;
    }
    public Guid OperationId { get; }
    public Guid LocalSubjectId { get; }
    public Guid PersonalActorId { get; }
    public Guid ExternalLoginId { get; }
    public LocalIdentityLifecyclePurpose Purpose { get; }
    public Guid Generation { get; }
}

public sealed record LocalIdentityLifecycleRequest
{
    public LocalIdentityLifecycleRequest(Guid localSubjectId, Guid personalActorId, Guid externalLoginId,
        LocalIdentityLifecyclePurpose purpose, string address, string? expectedSecurityStamp = null)
    {
        if (localSubjectId == Guid.Empty || personalActorId == Guid.Empty || externalLoginId == Guid.Empty
            || !Enum.IsDefined(purpose)) throw new ArgumentException("A complete Local account binding is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        LocalSubjectId = localSubjectId; PersonalActorId = personalActorId; ExternalLoginId = externalLoginId;
        Purpose = purpose; Address = address.Trim(); ExpectedSecurityStamp = expectedSecurityStamp;
    }
    public Guid LocalSubjectId { get; }
    public Guid PersonalActorId { get; }
    public Guid ExternalLoginId { get; }
    public LocalIdentityLifecyclePurpose Purpose { get; }
    public string Address { get; }
    public string? ExpectedSecurityStamp { get; }
    public override string ToString() => nameof(LocalIdentityLifecycleRequest);
}

public sealed record LocalIdentityLifecycleConsumption
{
    public LocalIdentityLifecycleConsumption(LocalIdentityLifecyclePointer operation, string token, string? newPassword = null)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Operation = operation; Token = token; NewPassword = newPassword;
    }
    public LocalIdentityLifecyclePointer Operation { get; }
    public string Token { get; }
    public string? NewPassword { get; }
    public override string ToString() => nameof(LocalIdentityLifecycleConsumption);
}

public sealed record LocalIdentityLifecycleTransport
{
    public LocalIdentityLifecycleTransport(LocalIdentityLifecyclePointer operation, string address, string token, DateTimeOffset expiresAtUtc)
    { Operation = operation; Address = address; Token = token; ExpiresAtUtc = expiresAtUtc; }
    public LocalIdentityLifecyclePointer Operation { get; }
    public string Address { get; }
    public string Token { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public override string ToString() => nameof(LocalIdentityLifecycleTransport);
}

public sealed record LocalIdentityLifecycleSynchronization
{
    public LocalIdentityLifecycleSynchronization(LocalIdentityLifecyclePointer operation, string? email, bool emailVerified,
        string firstName, string lastName, DateTimeOffset consumedAtUtc, bool synchronized)
    { Operation = operation; Email = email; EmailVerified = emailVerified; FirstName = firstName;
        LastName = lastName; ConsumedAtUtc = consumedAtUtc; Synchronized = synchronized; }
    public LocalIdentityLifecyclePointer Operation { get; }
    public Guid ApplicationUserId => Operation.LocalSubjectId;
    public string? Email { get; }
    public bool EmailVerified { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public DateTimeOffset ConsumedAtUtc { get; }
    public bool Synchronized { get; }
    public override string ToString() => nameof(LocalIdentityLifecycleSynchronization);
}

public sealed record LocalIdentityLifecycleResult
{
    public LocalIdentityLifecycleResult(LocalIdentityLifecycleOutcome outcome, LocalIdentityLifecycleSynchronization? synchronization = null)
    { Outcome = outcome; Synchronization = synchronization; }
    public LocalIdentityLifecycleOutcome Outcome { get; }
    public LocalIdentityLifecycleSynchronization? Synchronization { get; }
}
