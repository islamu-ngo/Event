// ABOUTME: Defines Local credential lifecycle mutations and immutable administrative identity and operation reads.
// ABOUTME: Separates validated nonsecret intent and durable audit from plaintext returned only to an issuance winner.

using System.Net.Mail;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Contracts.Identity;

public interface ILocalCredentialAdministration
{
    Task<LocalIdentityPage> ListAsync(LocalIdentityListRequest request, CancellationToken cancellationToken);

    Task<LocalCredentialOperationStatus?> ReadOperationAsync(Guid operationId, CancellationToken cancellationToken);

    Task<LocalCredentialCreateResult> CreatePendingAsync(
        LocalCredentialCreateRequest request,
        CancellationToken cancellationToken);

    Task<LocalCredentialProvisioningSnapshot?> ReadProvisioningAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<LocalCredentialActivationOutcome> ActivateChangeRequiredAsync(
        LocalCredentialActivationRequest request,
        CancellationToken cancellationToken);

    Task<LocalCredentialReplacementOutcome> ReplaceAsync(
        LocalCredentialReplacementRequest request,
        CancellationToken cancellationToken);

    Task<LocalCredentialResetResult> ResetAsync(
        LocalCredentialResetRequest request,
        CancellationToken cancellationToken);
}

public sealed record LocalIdentityListRequest
{
    public LocalIdentityListRequest(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
        _ = checked((pageNumber - 1) * pageSize);
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public int PageNumber { get; }
    public int PageSize { get; }
}

public sealed record LocalIdentityPage
{
    public LocalIdentityPage(IEnumerable<LocalIdentitySummary> items, int pageNumber, int pageSize, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (pageNumber < 1 || pageSize is < 1 or > 100 || totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Valid bounded pagination is required.");
        }
        _ = checked((pageNumber - 1) * pageSize);
        LocalIdentitySummary[] snapshot = items.ToArray();
        if (snapshot.Length > pageSize || snapshot.Any(item => item is null))
        {
            throw new ArgumentException("The identity page contains invalid items.", nameof(items));
        }
        Items = new ReadOnlyCollection<LocalIdentitySummary>(snapshot);
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<LocalIdentitySummary> Items { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalCount { get; }

    public override string ToString() => nameof(LocalIdentityPage);
}

public sealed record LocalIdentitySummary
{
    public LocalIdentitySummary(
        Guid localSubjectId, string? email, string? firstName, string? lastName, bool emailVerified,
        LocalCredentialState? credentialState, Guid? currentOperationId,
        Guid? currentOperationConcurrencyStamp, bool hasExactBinding)
    {
        if (localSubjectId == Guid.Empty)
        {
            throw new ArgumentException("A Local subject is required.", nameof(localSubjectId));
        }
        if (credentialState.HasValue && !Enum.IsDefined(credentialState.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(credentialState));
        }
        if (currentOperationId == Guid.Empty || currentOperationConcurrencyStamp == Guid.Empty
            || credentialState.HasValue != currentOperationId.HasValue
            || currentOperationId.HasValue != currentOperationConcurrencyStamp.HasValue
            || (hasExactBinding && !currentOperationId.HasValue))
        {
            throw new ArgumentException("Current credential metadata must be complete or absent.");
        }
        LocalSubjectId = localSubjectId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        EmailVerified = emailVerified;
        CredentialState = credentialState;
        CurrentOperationId = currentOperationId;
        CurrentOperationConcurrencyStamp = currentOperationConcurrencyStamp;
        HasExactBinding = hasExactBinding;
    }

    public Guid LocalSubjectId { get; }
    public string? Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
    public bool EmailVerified { get; }
    public LocalCredentialState? CredentialState { get; }
    public Guid? CurrentOperationId { get; }
    public Guid? CurrentOperationConcurrencyStamp { get; }
    [JsonIgnore]
    public bool HasExactBinding { get; }

    public override string ToString() => nameof(LocalIdentitySummary);
}

public sealed record LocalCredentialOperationStatus
{
    public LocalCredentialOperationStatus(
        LocalCredentialOperationReceipt receipt, Guid operationConcurrencyStamp,
        Guid verifiedByApplicationUserId, DateTime verifiedAt, DateTime? updatedAt,
        bool isCurrent, LocalCredentialState? credentialState, LocalCredentialResetReceipt? resetAudit)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (operationConcurrencyStamp == Guid.Empty || verifiedByApplicationUserId == Guid.Empty)
        {
            throw new ArgumentException("Operation and verification identifiers are required.");
        }
        if (verifiedAt.Kind != DateTimeKind.Utc || (updatedAt.HasValue && updatedAt.Value.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException("UTC audit times are required.");
        }
        if ((credentialState.HasValue && !Enum.IsDefined(credentialState.Value))
            || isCurrent != credentialState.HasValue)
        {
            throw new ArgumentException("Current status requires valid credential state.", nameof(credentialState));
        }
        if (isCurrent && !((credentialState == LocalCredentialState.ProvisioningPending
                    && receipt.Kind == LocalCredentialOperationKind.Create
                    && receipt.Stage == LocalCredentialOperationStage.ProvisioningPending)
                || (credentialState == LocalCredentialState.ChangeRequired
                    && receipt.Stage == LocalCredentialOperationStage.ChangeRequired)
                || (credentialState == LocalCredentialState.Ready
                    && receipt.Stage == LocalCredentialOperationStage.Replaced)))
        {
            throw new ArgumentException("Current credential state must agree with its operation stage.", nameof(credentialState));
        }
        if ((receipt.Kind == LocalCredentialOperationKind.Reset) != (resetAudit is not null)
            || (resetAudit is not null && resetAudit.Operation != receipt))
        {
            throw new ArgumentException("Reset audit must belong to the exact operation.", nameof(resetAudit));
        }
        Receipt = receipt;
        OperationConcurrencyStamp = operationConcurrencyStamp;
        VerifiedByApplicationUserId = verifiedByApplicationUserId;
        VerifiedAt = verifiedAt;
        UpdatedAt = updatedAt;
        IsCurrent = isCurrent;
        CredentialState = credentialState;
        ResetAudit = resetAudit;
    }

    public LocalCredentialOperationReceipt Receipt { get; }
    public Guid OperationConcurrencyStamp { get; }
    public Guid VerifiedByApplicationUserId { get; }
    public DateTime VerifiedAt { get; }
    public DateTime? UpdatedAt { get; }
    public bool IsCurrent { get; }
    public LocalCredentialState? CredentialState { get; }
    public LocalCredentialResetReceipt? ResetAudit { get; }

    public override string ToString() => nameof(LocalCredentialOperationStatus);
}

public enum LocalCredentialResetOutcome
{
    Reset = 1,
    Replayed = 2,
    Conflict = 3,
    NotFound = 4,
    BindingIncomplete = 5,
    Invalid = 6
}

public sealed record LocalCredentialResetRequest
{
    public const int MaximumReasonLength = 1000;

    public LocalCredentialResetRequest(
        Guid operationId,
        Guid initiatingApplicationUserId,
        Guid localSubjectId,
        Guid expectedCurrentOperationId,
        Guid expectedCurrentOperationConcurrencyStamp,
        string reason)
    {
        if (operationId == Guid.Empty || initiatingApplicationUserId == Guid.Empty
            || localSubjectId == Guid.Empty || expectedCurrentOperationId == Guid.Empty
            || expectedCurrentOperationConcurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("Credential reset identifiers must be nonempty.");
        }
        if (operationId == expectedCurrentOperationId)
        {
            throw new ArgumentException("A reset requires a new operation identifier.", nameof(operationId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string normalizedReason = reason.Trim();
        if (normalizedReason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("The reset reason exceeds its permitted length.", nameof(reason));
        }

        OperationId = operationId;
        InitiatingApplicationUserId = initiatingApplicationUserId;
        LocalSubjectId = localSubjectId;
        ExpectedCurrentOperationId = expectedCurrentOperationId;
        ExpectedCurrentOperationConcurrencyStamp = expectedCurrentOperationConcurrencyStamp;
        Reason = normalizedReason;
    }

    public Guid OperationId { get; }
    public Guid InitiatingApplicationUserId { get; }
    public Guid LocalSubjectId { get; }
    public Guid ExpectedCurrentOperationId { get; }
    public Guid ExpectedCurrentOperationConcurrencyStamp { get; }
    public string Reason { get; }

    public override string ToString() => nameof(LocalCredentialResetRequest);
}

public sealed record LocalCredentialResetReceipt
{
    public LocalCredentialResetReceipt(
        LocalCredentialOperationReceipt operation,
        Guid previousOperationId,
        Guid previousOperationConcurrencyStamp,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != LocalCredentialOperationKind.Reset
            || operation.Stage is not (LocalCredentialOperationStage.ChangeRequired
                or LocalCredentialOperationStage.Replaced or LocalCredentialOperationStage.Superseded
                or LocalCredentialOperationStage.Abandoned))
        {
            throw new ArgumentException("A reset operation receipt is required.", nameof(operation));
        }
        if (previousOperationId == Guid.Empty || previousOperationConcurrencyStamp == Guid.Empty
            || previousOperationId == operation.OperationId)
        {
            throw new ArgumentException("A distinct previous operation and concurrency stamp are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string normalizedReason = reason.Trim();
        if (normalizedReason.Length > LocalCredentialResetRequest.MaximumReasonLength)
        {
            throw new ArgumentException("The reset reason exceeds its permitted length.", nameof(reason));
        }

        Operation = operation;
        PreviousOperationId = previousOperationId;
        PreviousOperationConcurrencyStamp = previousOperationConcurrencyStamp;
        Reason = normalizedReason;
    }

    public LocalCredentialOperationReceipt Operation { get; }
    public Guid PreviousOperationId { get; }
    public Guid PreviousOperationConcurrencyStamp { get; }
    public string Reason { get; }

    public override string ToString() => nameof(LocalCredentialResetReceipt);
}

public sealed class LocalCredentialResetResult
{
    private LocalCredentialResetResult(
        LocalCredentialResetOutcome outcome,
        LocalCredentialResetReceipt? receipt,
        string? temporaryPassword)
    {
        Outcome = outcome;
        Receipt = receipt;
        TemporaryPassword = temporaryPassword;
    }

    public LocalCredentialResetOutcome Outcome { get; }
    public LocalCredentialResetReceipt? Receipt { get; }
    public string? TemporaryPassword { get; }

    public static LocalCredentialResetResult Reset(LocalCredentialResetReceipt receipt, string temporaryPassword)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPassword);
        if (receipt.Operation.Stage != LocalCredentialOperationStage.ChangeRequired)
        {
            throw new ArgumentException("A change-required reset receipt is required.", nameof(receipt));
        }
        return new LocalCredentialResetResult(
            outcome: LocalCredentialResetOutcome.Reset, receipt: receipt, temporaryPassword: temporaryPassword);
    }

    public static LocalCredentialResetResult Replayed(LocalCredentialResetReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new LocalCredentialResetResult(
            outcome: LocalCredentialResetOutcome.Replayed, receipt: receipt, temporaryPassword: null);
    }

    public static LocalCredentialResetResult Rejected(LocalCredentialResetOutcome outcome)
    {
        if (outcome is not (LocalCredentialResetOutcome.Conflict or LocalCredentialResetOutcome.NotFound
            or LocalCredentialResetOutcome.BindingIncomplete or LocalCredentialResetOutcome.Invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A rejected outcome is required.");
        }
        return new LocalCredentialResetResult(outcome: outcome, receipt: null, temporaryPassword: null);
    }

    public override string ToString() => nameof(LocalCredentialResetResult);
}

public enum LocalCredentialReplacementOutcome
{
    Replaced = 1,
    InvalidChallenge = 2,
    SamePassword = 3,
    InvalidPassword = 4,
    Conflict = 5
}

public sealed record LocalCredentialReplacementAuthority
{
    public LocalCredentialReplacementAuthority(
        LocalCredentialReplacementSubject subject,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (issuedAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero
            || expiresAtUtc <= issuedAtUtc
            || expiresAtUtc - issuedAtUtc > LocalCredentialChallengeToken.MaximumLifetime)
        {
            throw new ArgumentException("Replacement authority requires a positive UTC lifetime of at most five minutes.");
        }
        Subject = subject;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public LocalCredentialReplacementSubject Subject { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public override string ToString() => nameof(LocalCredentialReplacementAuthority);
}

public sealed record LocalCredentialReplacementRequest
{
    public LocalCredentialReplacementRequest(LocalCredentialReplacementAuthority authority, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        Authority = authority;
        NewPassword = newPassword;
    }

    public LocalCredentialReplacementAuthority Authority { get; }
    public string NewPassword { get; }

    public override string ToString() => nameof(LocalCredentialReplacementRequest);
}

public enum LocalCredentialCreateOutcome
{
    Created = 1,
    Replayed = 2,
    Conflict = 3,
    Invalid = 4
}

public enum LocalCredentialActivationOutcome
{
    Activated = 1,
    AlreadyActivated = 2,
    Conflict = 3,
    BindingIncomplete = 4,
    NotFound = 5
}

public sealed record LocalCredentialActivationRequest
{
    public LocalCredentialActivationRequest(Guid operationId, Guid expectedOperationConcurrencyStamp)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        if (expectedOperationConcurrencyStamp == Guid.Empty)
            throw new ArgumentException("An operation concurrency stamp is required.", nameof(expectedOperationConcurrencyStamp));

        OperationId = operationId;
        ExpectedOperationConcurrencyStamp = expectedOperationConcurrencyStamp;
    }

    public Guid OperationId { get; }
    public Guid ExpectedOperationConcurrencyStamp { get; }

    public override string ToString() => nameof(LocalCredentialActivationRequest);
}

public sealed record LocalCredentialProvisioningSnapshot
{
    public LocalCredentialProvisioningSnapshot(
        LocalCredentialOperationReceipt receipt,
        Guid operationConcurrencyStamp,
        string email,
        string firstName,
        string lastName,
        bool emailVerified)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (operationConcurrencyStamp == Guid.Empty)
            throw new ArgumentException("An operation concurrency stamp is required.", nameof(operationConcurrencyStamp));
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentNullException.ThrowIfNull(lastName);

        Receipt = receipt;
        OperationConcurrencyStamp = operationConcurrencyStamp;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        EmailVerified = emailVerified;
    }

    public LocalCredentialOperationReceipt Receipt { get; }
    public Guid OperationConcurrencyStamp { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public bool EmailVerified { get; }

    public override string ToString() => nameof(LocalCredentialProvisioningSnapshot);
}

public enum LocalCredentialOperationKind
{
    Create = 1,
    Reset = 2
}

public enum LocalCredentialOperationStage
{
    ProvisioningPending = 1,
    ChangeRequired = 2,
    Replaced = 3,
    Superseded = 4,
    Abandoned = 5
}

public sealed record LocalCredentialCreateRequest
{
    public LocalCredentialCreateRequest(
        Guid operationId,
        Guid initiatingApplicationUserId,
        string email,
        string firstName,
        string lastName)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        if (initiatingApplicationUserId == Guid.Empty)
            throw new ArgumentException("An initiating application user is required.", nameof(initiatingApplicationUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentNullException.ThrowIfNull(lastName);

        string normalizedEmail = email.Trim().ToLowerInvariant();
        if (normalizedEmail.Length > 256
            || !MailAddress.TryCreate(normalizedEmail, out MailAddress? address)
            || !string.Equals(address.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid bounded email address is required.", nameof(email));
        }

        string normalizedFirstName = firstName.Trim();
        string normalizedLastName = lastName.Trim();
        if (normalizedFirstName.Length > 200)
            throw new ArgumentException("The first name exceeds its limit.", nameof(firstName));
        if (normalizedLastName.Length > 200)
            throw new ArgumentException("The last name exceeds its limit.", nameof(lastName));

        OperationId = operationId;
        InitiatingApplicationUserId = initiatingApplicationUserId;
        Email = normalizedEmail;
        FirstName = normalizedFirstName;
        LastName = normalizedLastName;
    }

    public Guid OperationId { get; }
    public Guid InitiatingApplicationUserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    public override string ToString() => nameof(LocalCredentialCreateRequest);
}

public sealed record LocalCredentialOperationReceipt
{
    public LocalCredentialOperationReceipt(
        Guid operationId,
        LocalCredentialOperationKind kind,
        LocalCredentialOperationStage stage,
        Guid initiatingApplicationUserId,
        Guid localSubjectId,
        Guid personalActorId,
        Guid externalLoginId,
        DateTime createdAt)
    {
        if (operationId == Guid.Empty || initiatingApplicationUserId == Guid.Empty
            || localSubjectId == Guid.Empty || personalActorId == Guid.Empty || externalLoginId == Guid.Empty)
        {
            throw new ArgumentException("Credential receipt identifiers must be nonempty.");
        }

        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Unknown credential operation kind.");
        if (!Enum.IsDefined(stage))
            throw new ArgumentOutOfRangeException(nameof(stage), "Unknown credential operation stage.");
        if (createdAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A UTC creation time is required.", nameof(createdAt));

        OperationId = operationId;
        Kind = kind;
        Stage = stage;
        InitiatingApplicationUserId = initiatingApplicationUserId;
        LocalSubjectId = localSubjectId;
        PersonalActorId = personalActorId;
        ExternalLoginId = externalLoginId;
        CreatedAt = createdAt;
    }

    public Guid OperationId { get; }
    public LocalCredentialOperationKind Kind { get; }
    public LocalCredentialOperationStage Stage { get; }
    public Guid InitiatingApplicationUserId { get; }
    public Guid LocalSubjectId { get; }
    public Guid ApplicationUserId => LocalSubjectId;
    public Guid PersonalActorId { get; }
    public Guid ExternalLoginId { get; }
    public DateTime CreatedAt { get; }

    public override string ToString() => nameof(LocalCredentialOperationReceipt);
}

public sealed class LocalCredentialCreateResult
{
    private LocalCredentialCreateResult(
        LocalCredentialCreateOutcome outcome,
        LocalCredentialOperationReceipt? receipt,
        string? temporaryPassword)
    {
        Outcome = outcome;
        Receipt = receipt;
        TemporaryPassword = temporaryPassword;
    }

    public LocalCredentialCreateOutcome Outcome { get; }
    public LocalCredentialOperationReceipt? Receipt { get; }
    public string? TemporaryPassword { get; }

    public static LocalCredentialCreateResult Created(
        LocalCredentialOperationReceipt receipt,
        string temporaryPassword)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPassword);
        if (receipt.Kind != LocalCredentialOperationKind.Create
            || receipt.Stage != LocalCredentialOperationStage.ProvisioningPending)
        {
            throw new ArgumentException("A pending creation receipt is required.", nameof(receipt));
        }

        return new LocalCredentialCreateResult(
            outcome: LocalCredentialCreateOutcome.Created,
            receipt: receipt,
            temporaryPassword: temporaryPassword);
    }

    public static LocalCredentialCreateResult Replayed(LocalCredentialOperationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new LocalCredentialCreateResult(
            outcome: LocalCredentialCreateOutcome.Replayed,
            receipt: receipt,
            temporaryPassword: null);
    }

    public static LocalCredentialCreateResult Rejected(LocalCredentialCreateOutcome outcome)
    {
        if (outcome is not (LocalCredentialCreateOutcome.Conflict
            or LocalCredentialCreateOutcome.Invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A rejected outcome is required.");
        }

        return new LocalCredentialCreateResult(outcome: outcome, receipt: null, temporaryPassword: null);
    }

    public override string ToString() => nameof(LocalCredentialCreateResult);
}
