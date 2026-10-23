using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class AuthorizeRecognitionRecoveryCommandHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : ICommandHandler<AuthorizeRecognitionRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AuthorizeRecognitionRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null) return BaseCommandResponse.Authentication<Guid>();
        var validation = await new AuthorizeRecognitionRecoveryCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        try
        {
            var row = await recovery.AuthorizeAsync(command.Id, command.ExpectedRevision, cancellationToken);
            return BaseCommandResponse.Success(row.Id);
        }
        catch (UnauthorizedAccessException) { return BaseCommandResponse.Authorization<Guid>(); }
        catch (InvalidOperationException exception) when (exception.Message is
            "moderation_recognition_disposition_owner_unavailable" or "moderation_recognition_disposition_qualification_unavailable")
        { return BaseCommandResponse.Failure<Guid>(RecognitionRecoveryMapping.Unavailable); }
        catch (InvalidOperationException) { return BaseCommandResponse.Conflict(command.Id); }
    }
}
