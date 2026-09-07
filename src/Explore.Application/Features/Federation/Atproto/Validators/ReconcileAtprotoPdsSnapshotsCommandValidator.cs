using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Federation.Atproto.Validators;

public sealed class ReconcileAtprotoPdsSnapshotsCommandValidator
    : AbstractValidator<ReconcileAtprotoPdsSnapshotsCommand>
{
    public const int MaximumProtocolDids = 10_000;

    public ReconcileAtprotoPdsSnapshotsCommandValidator()
    {
        RuleFor(command => command.Claim.ConsumerStateId).NotEmpty();
        RuleFor(command => command.Claim.LeaseToken).NotEmpty();
        RuleFor(command => command.Claim.LeaseFence).GreaterThan(0);
        RuleFor(command => command.Claim.Service).NotEmpty().MaximumLength(500);
        RuleFor(command => command.SnapshotStartedAt)
            .Must(value => value.Kind == DateTimeKind.Utc && value > DateTime.UnixEpoch);
        RuleFor(command => command.AllowedDids).NotNull();
        RuleForEach(command => command.AllowedDids).Must(IsValidDid);
        RuleFor(command => command.AllowedDids)
            .Must(dids => dids.Count <= MaximumProtocolDids);
        RuleFor(command => command.LastCompletedFingerprint)
            .Matches("^[0-9a-f]{64}$")
            .When(command => command.LastCompletedFingerprint is not null);
    }

    private static bool IsValidDid(string? did) =>
        did is { Length: > 4 and <= 255 }
        && did.StartsWith("did:", StringComparison.Ordinal)
        && !did.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
}
