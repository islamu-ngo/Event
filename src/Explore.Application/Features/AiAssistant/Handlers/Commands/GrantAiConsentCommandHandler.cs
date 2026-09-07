using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class GrantAiConsentCommandHandler : IRequestHandler<GrantAiConsentCommand, BaseCommandResponse<Guid>>
{
    private readonly IAiConsentGrantRepository _consentRepository;

    public GrantAiConsentCommandHandler(IAiConsentGrantRepository consentRepository)
    {
        _consentRepository = consentRepository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(GrantAiConsentCommand request, CancellationToken cancellationToken)
    {
        var validator = new GrantAiConsentCommandValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                "validation_failed",
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var existing = await _consentRepository.FindActiveGrantAsync(
            request.SubjectUserId,
            request.EntityName,
            request.FieldName,
            request.ProviderTrustTierId,
            cancellationToken);

        if (existing is not null)
        {
            return Failure(
                "active_consent_exists",
                "An active consent grant already exists for this entity, field, and provider trust tier.");
        }

        var utcNow = DateTimeOffset.UtcNow;
        var grant = new AiConsentGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            SubjectUserId = request.SubjectUserId,
            EntityName = request.EntityName,
            FieldName = request.FieldName,
            ProviderTrustTierId = request.ProviderTrustTierId,
            StatusId = (int)AiConsentGrantStatusEnum.Granted,
            Purpose = request.Purpose,
            GrantedAtUtc = utcNow,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = request.GrantedByUserId,
        };

        await _consentRepository.AddAsync(grant, cancellationToken);

        return Success(grant.Id, "AI consent grant created.");
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string failureCode, string message) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, [message], Guid.Empty);
}
