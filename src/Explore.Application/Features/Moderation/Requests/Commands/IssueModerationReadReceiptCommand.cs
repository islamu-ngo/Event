using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>A lookup for an independently issued grant; caller and current case/material authority are resolved natively.</summary>
public sealed record IssueModerationReadReceiptCommand : ICommand<ModerationReadReceiptResponse>
{
    public required Guid ReadGrantId { get; init; }
    public override string ToString() => nameof(IssueModerationReadReceiptCommand);
}

/// <summary>Only the committed account-bound read receipt, opaque token and expiry; never the confidential case payload.</summary>
public sealed record ModerationReadReceiptResponse : BaseCommandResponse<Guid>
{
    private readonly byte[] _presentedToken;

    private ModerationReadReceiptResponse(
        BaseCommandResponse<Guid> state, ReadOnlyMemory<byte> token = default, DateTimeOffset? expiresAt = null)
        : base(state, true)
    {
        _presentedToken = token.ToArray();
        ExpiresAt = expiresAt;
    }

    public ReadOnlyMemory<byte> PresentedToken => _presentedToken.ToArray();
    public DateTimeOffset? ExpiresAt { get; }

    internal static ModerationReadReceiptResponse FromCommitted(ModerationCaseIssuedReceipt? committed) =>
        committed is null
            ? new(BaseCommandResponse.Failure<Guid>("ModerationCaseIntakeUnavailable"))
            : new(BaseCommandResponse.Success(committed.Receipt.Id), committed.Token, committed.Receipt.ExpiresAt);

    internal static ModerationReadReceiptResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));

    public override string ToString() => nameof(ModerationReadReceiptResponse);
}
