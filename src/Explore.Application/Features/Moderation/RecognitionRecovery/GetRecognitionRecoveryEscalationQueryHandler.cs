using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class GetRecognitionRecoveryEscalationQueryHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : IQueryHandler<GetRecognitionRecoveryEscalationQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>>
{
    public async Task<RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>> QueryAsync(
        GetRecognitionRecoveryEscalationQuery query, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null)
            return RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Failed(BaseCommandResponse.Authentication<Guid>());
        var validation = await new GetRecognitionRecoveryEscalationQueryValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Failed(
                BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage)));
        try
        {
            var row = await recovery.ReadEscalationAsync(query.Id, cancellationToken);
            return row is null
                ? RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Failed(BaseCommandResponse.NotFound<Guid>())
                : RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Found(row.Id, RecognitionRecoveryMapping.ToDto(row));
        }
        catch (UnauthorizedAccessException)
        { return RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Failed(BaseCommandResponse.Authorization<Guid>()); }
        catch (InvalidOperationException)
        { return RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>.Failed(BaseCommandResponse.Failure<Guid>(RecognitionRecoveryMapping.Unavailable)); }
    }
}
