using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class AssessRecognitionRecoveryCommandHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : ICommandHandler<AssessRecognitionRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AssessRecognitionRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null) return BaseCommandResponse.Authentication<Guid>();
        var validation = await new AssessRecognitionRecoveryCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        try
        {
            var row = await recovery.AssessAsync(new(command.OperationId, command.IntentId, command.ExpectedPendingRevision,
                command.SourceId, command.ExpectedSourceRevision, command.ExpectedSourceGeneration, command.AdoptionId,
                command.ExpectedAdoptionRevision, command.ExpectedAdoptionGeneration, command.Grounds, command.ValidUntil),
                cancellationToken);
            return BaseCommandResponse.Success(row.Id);
        }
        catch (UnauthorizedAccessException) { return BaseCommandResponse.Authorization<Guid>(); }
        catch (InvalidOperationException exception) when (exception.Message is
            "moderation_recognition_disposition_owner_unavailable" or "moderation_recognition_disposition_qualification_unavailable")
        { return BaseCommandResponse.Failure<Guid>(RecognitionRecoveryMapping.Unavailable); }
        catch (InvalidOperationException) { return BaseCommandResponse.Conflict(command.OperationId); }
        catch (ArgumentException) { return BaseCommandResponse.Validation<Guid>(["Invalid recovery assessment."]); }
    }
}
