using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class AcknowledgeRecognitionRecoveryCommandHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : ICommandHandler<AcknowledgeRecognitionRecoveryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AcknowledgeRecognitionRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null) return BaseCommandResponse.Authentication<Guid>();
        var validation = await new AcknowledgeRecognitionRecoveryCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        try
        {
            await recovery.AcknowledgeAsync(command.Id, cancellationToken);
            return BaseCommandResponse.Success(command.Id);
        }
        catch (UnauthorizedAccessException) { return BaseCommandResponse.Authorization<Guid>(); }
        catch (InvalidOperationException) { return BaseCommandResponse.Conflict(command.Id); }
    }
}
