using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class GetRecognitionRecoveryContextQueryHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : IQueryHandler<GetRecognitionRecoveryContextQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>>
{
    public async Task<RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>> QueryAsync(
        GetRecognitionRecoveryContextQuery query, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null)
            return RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Failed(BaseCommandResponse.Authentication<Guid>());
        var validation = await new GetRecognitionRecoveryContextQueryValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Failed(
                BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage)));
        try
        {
            var row = await recovery.ReadOwnerContextAsync(query.EscalationId, cancellationToken);
            return row is null
                ? RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Failed(BaseCommandResponse.NotFound<Guid>())
                : RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Found(row.EscalationId, RecognitionRecoveryMapping.ToDto(row));
        }
        catch (UnauthorizedAccessException)
        { return RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Failed(BaseCommandResponse.Authorization<Guid>()); }
        catch (InvalidOperationException)
        { return RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>.Failed(BaseCommandResponse.Failure<Guid>(RecognitionRecoveryMapping.Unavailable)); }
    }
}
