using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Responses;

public sealed record SubmitModerationContentResponseCommand(
    Guid NoticeId, Guid OperationId, string SnapshotHash,
    Guid AssignmentId, long AssignmentRevision, string Statement) : ICommand<BaseCommandResponse<Guid>>
{
    public override string ToString() => nameof(SubmitModerationContentResponseCommand);
}

public sealed class SubmitModerationContentResponseCommandValidator : AbstractValidator<SubmitModerationContentResponseCommand>
{
    public SubmitModerationContentResponseCommandValidator()
    {
        RuleFor(command => command.NoticeId).NotEmpty();
        RuleFor(command => command.OperationId)
            .Must(id => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(command => command.AssignmentId).NotEmpty();
        RuleFor(command => command.AssignmentRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.SnapshotHash)
            .Must(hash => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitLower));
        RuleFor(command => command.Statement)
            .Must(statement => !string.IsNullOrWhiteSpace(statement)
                && Encoding.UTF8.GetByteCount(statement) <= ModerationContentResponse.MaximumStatementBytes)
            .WithMessage("A finite protected response statement is required.");
    }
}

public sealed class SubmitModerationContentResponseCommandHandler(
    IModerationContentResponseRepository repository, ITenantContext tenant)
    : ICommandHandler<SubmitModerationContentResponseCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        SubmitModerationContentResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new SubmitModerationContentResponseCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.SubmitContentResponseAsync(new(tenant.TenantId, command.NoticeId,
            command.OperationId, command.SnapshotHash, command.AssignmentId, command.AssignmentRevision,
            command.Statement), cancellationToken);
        return result.Outcome == ModerationQualificationWriteOutcome.Known && result.Response is { } response
            ? BaseCommandResponse.Success(response.Id)
            : ModerationContentResponseAvailabilityResult.Failure(result.Outcome, command.OperationId);
    }
}
