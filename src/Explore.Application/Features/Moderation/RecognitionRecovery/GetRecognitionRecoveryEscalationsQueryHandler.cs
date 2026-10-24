using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class GetRecognitionRecoveryEscalationsQueryHandler(ModerationRecognitionRecovery recovery, IUserContext user)
    : IQueryHandler<GetRecognitionRecoveryEscalationsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>>
{
    public async Task<RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>> QueryAsync(
        GetRecognitionRecoveryEscalationsQuery query, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId is null)
            return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>
                .Failed(BaseCommandResponse.Authentication<Guid>());
        var validation = await new GetRecognitionRecoveryEscalationsQueryValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>.Failed(
                BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage)));
        try
        {
            var rows = await recovery.ReadEscalationsAsync(query.AfterId, query.Limit, cancellationToken);
            return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>.Found(Guid.Empty,
                Array.AsReadOnly(rows.Select(RecognitionRecoveryMapping.ToDto).ToArray()));
        }
        catch (UnauthorizedAccessException)
        { return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>.Failed(BaseCommandResponse.Authorization<Guid>()); }
        catch (InvalidOperationException)
        { return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>.Failed(BaseCommandResponse.Failure<Guid>(RecognitionRecoveryMapping.Unavailable)); }
    }
}
