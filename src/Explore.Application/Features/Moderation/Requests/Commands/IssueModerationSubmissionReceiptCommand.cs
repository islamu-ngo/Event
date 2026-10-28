using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Untrusted target lookup and material; the native owner establishes current account and relationship authority.</summary>
public sealed record IssueModerationSubmissionReceiptCommand : ICommand<ModerationSubmissionReceiptResponse>
{
    private byte[] _occurrenceMaterial = [];
    private byte[] _material = [];

    public required ModerationCaseTargetKind Kind { get; init; }
    public required ModerationCaseTargetKind NativeKind { get; init; }
    public Guid? EventId { get; init; }
    public required Guid NativeTargetId { get; init; }
    public required ModerationCaseRelationshipKind RelationshipKind { get; init; }
    public required Guid RelationshipSourceId { get; init; }
    public required ReadOnlyMemory<byte> OccurrenceMaterial
    {
        get => _occurrenceMaterial.ToArray();
        init => _occurrenceMaterial = value.ToArray();
    }
    public required ReadOnlyMemory<byte> Material
    {
        get => _material.ToArray();
        init => _material = value.ToArray();
    }

    internal ModerationCaseTargetIssueRequest ToRequest() => new(
        Kind, NativeKind, EventId, NativeTargetId, RelationshipKind, RelationshipSourceId,
        OccurrenceMaterial, Material);

    public override string ToString() => nameof(IssueModerationSubmissionReceiptCommand);
}

/// <summary>Committed, finite submission token only; no confidential target, case status or responsible-human attribution.</summary>
public sealed record ModerationSubmissionReceiptResponse : BaseCommandResponse<Guid>
{
    private readonly byte[] _presentedToken;

    private ModerationSubmissionReceiptResponse(
        BaseCommandResponse<Guid> state, ReadOnlyMemory<byte> token = default, DateTimeOffset? expiresAt = null)
        : base(state, true)
    {
        _presentedToken = token.ToArray();
        ExpiresAt = expiresAt;
    }

    public ReadOnlyMemory<byte> PresentedToken => _presentedToken.ToArray();
    public DateTimeOffset? ExpiresAt { get; }

    internal static ModerationSubmissionReceiptResponse FromCommitted(ModerationCaseIssuedReceipt? committed) =>
        committed is null
            ? new(BaseCommandResponse.Failure<Guid>("ModerationCaseIntakeUnavailable"))
            : new(BaseCommandResponse.Success(committed.Receipt.Id), committed.Token, committed.Receipt.ExpiresAt);

    internal static ModerationSubmissionReceiptResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));

    public override string ToString() => nameof(ModerationSubmissionReceiptResponse);
}
