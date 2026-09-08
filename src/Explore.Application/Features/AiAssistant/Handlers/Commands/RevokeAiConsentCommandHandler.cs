using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class RevokeAiConsentCommandHandler : IRequestHandler<RevokeAiConsentCommand, BaseCommandResponse<Guid>>
{
    private readonly IAiConsentGrantRepository _consentRepository;
    private readonly IAiContextHygieneService _hygieneService;

    public RevokeAiConsentCommandHandler(
        IAiConsentGrantRepository consentRepository,
        IAiContextHygieneService hygieneService)
    {
        _consentRepository = consentRepository;
        _hygieneService = hygieneService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(RevokeAiConsentCommand request, CancellationToken cancellationToken)
    {
        if (request.GrantId == Guid.Empty)
        {
            return Failure("validation_failed", "GrantId is required.");
        }

        var grant = await _consentRepository.GetByIdForUpdateAsync(request.GrantId, cancellationToken);
        if (grant is null)
        {
            return Failure("not_found", "Consent grant not found.");
        }

        if (grant.StatusId != (int)Domain.Enums.AiConsentGrantStatusEnum.Granted)
        {
            return Failure("not_active", "Consent grant is not in Granted status.", request.GrantId);
        }

        var revokedAtUtc = DateTime.UtcNow;
        await _consentRepository.RevokeAsync(request.GrantId, request.RevokedByUserId, revokedAtUtc, cancellationToken);

        await _hygieneService.PropagateConsentRevocationAsync(
            grant.SubjectUserId,
            grant.EntityName,
            grant.FieldName,
            piiDisclosureEnabled: false,
            cancellationToken);

        return Success(request.GrantId, "AI consent grant revoked.");
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string failureCode, string message, Guid id = default) =>
        failureCode == FailureCodes.NotFound
            ? BaseCommandResponse.NotFound<Guid>(message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, [message], id);
}
