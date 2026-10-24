using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Notices;

/// <summary>Client intent only; current self-recipient authority is resolved at actual disclosure.</summary>
public sealed record ReadModerationNoticeCorrectionQuery : IQuery<ModerationNoticeCorrectionReadResult>
{
    public required Guid PendingEffectId { get; init; }
    public required string ExpectedPendingSnapshotHash { get; init; }
    public required string ExpectedPendingEffectBindingHash { get; init; }
    public required Guid ApprovalId { get; init; }
    public required Guid GrantId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid OriginalNoticeId { get; init; }
    public override string ToString() => nameof(ReadModerationNoticeCorrectionQuery);
}

/// <summary>A single-use pending operation, not prepared output or an authorization decision.</summary>
public sealed class ModerationNoticeCorrectionReadResult
{
    private readonly IModerationNoticeCorrectionDisclosure? disclosure;
    private readonly ModerationNoticeCorrectionReadRequest? request;
    private readonly CancellationToken queryCancellationToken;
    private int consumed;

    private ModerationNoticeCorrectionReadResult(IModerationNoticeCorrectionDisclosure? disclosure,
        ModerationNoticeCorrectionReadRequest? request, IEnumerable<string> errors,
        bool authenticationRequired, CancellationToken queryCancellationToken)
    {
        this.disclosure = disclosure;
        this.request = request;
        this.queryCancellationToken = queryCancellationToken;
        ValidationErrors = Array.AsReadOnly(errors.ToArray());
        AuthenticationRequired = authenticationRequired;
    }

    public bool IsValid => disclosure is not null && request is not null;
    public IReadOnlyList<string> ValidationErrors { get; }
    public bool AuthenticationRequired { get; }

    internal static ModerationNoticeCorrectionReadResult Invalid(IEnumerable<string> errors) =>
        new(null, null, errors, false, default);

    internal static ModerationNoticeCorrectionReadResult Unauthenticated() =>
        new(null, null, [], true, default);

    internal static ModerationNoticeCorrectionReadResult Prepare(IModerationNoticeCorrectionDisclosure disclosure,
        ReadModerationNoticeCorrectionQuery query, CancellationToken cancellationToken) =>
        new(disclosure, new(query.PendingEffectId, query.ExpectedPendingSnapshotHash,
            query.ExpectedPendingEffectBindingHash, query.ApprovalId, query.GrantId, query.CaseId,
            query.ExpectedCaseRevision, query.OriginalNoticeId), [], false, cancellationToken);

    public async Task<bool> DiscloseAsync(Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || disclosure is null || request is null)
            return false;
        ArgumentNullException.ThrowIfNull(write);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(queryCancellationToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await disclosure.DiscloseAsync(request, write, linked.Token);
    }

    public override string ToString() => nameof(ModerationNoticeCorrectionReadResult);
}

public sealed class ReadModerationNoticeCorrectionQueryHandler(
    IModerationNoticeCorrectionDisclosure disclosure, IUserContext user)
    : IQueryHandler<ReadModerationNoticeCorrectionQuery, ModerationNoticeCorrectionReadResult>
{
    public async Task<ModerationNoticeCorrectionReadResult> QueryAsync(
        ReadModerationNoticeCorrectionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!validation.IsValid)
            return ModerationNoticeCorrectionReadResult.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return user.IsAuthenticated
            ? ModerationNoticeCorrectionReadResult.Prepare(disclosure, query, cancellationToken)
            : ModerationNoticeCorrectionReadResult.Unauthenticated();
    }
}

public sealed class ReadModerationNoticeCorrectionQueryValidator : AbstractValidator<ReadModerationNoticeCorrectionQuery>
{
    public ReadModerationNoticeCorrectionQueryValidator()
    {
        RuleFor(query => query.PendingEffectId).Must(IsNativeUuid7);
        RuleFor(query => query.ApprovalId).Must(IsNativeUuid7);
        RuleFor(query => query.GrantId).Must(IsNativeUuid7);
        RuleFor(query => query.CaseId).Must(IsNativeUuid7);
        RuleFor(query => query.OriginalNoticeId).Must(IsNativeUuid7);
        RuleFor(query => query.ExpectedPendingSnapshotHash).Must(IsLowerHexHash);
        RuleFor(query => query.ExpectedPendingEffectBindingHash).Must(IsLowerHexHash);
        RuleFor(query => query.ExpectedCaseRevision).GreaterThan(0).LessThan(long.MaxValue);
    }

    private static bool IsNativeUuid7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool IsLowerHexHash(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
