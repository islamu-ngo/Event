namespace Explore.Application.Features.InstanceOnboarding.Commands;

using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Command to save candidate instance operator identity settings.
/// </summary>
public sealed record SaveInstanceOperatorIdentityCommand : ICommand<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>
{
    public SaveInstanceOperatorIdentityRequestDto Request { get; init; } = new();
}

/// <summary>
/// Handler for <see cref="SaveInstanceOperatorIdentityCommand"/>.
/// </summary>
public sealed class SaveInstanceOperatorIdentityCommandHandler(
    InstanceOperatorIdentityService identityService)
    : ICommandHandler<SaveInstanceOperatorIdentityCommand, BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>
{
    public async Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>> ExecuteAsync(
        SaveInstanceOperatorIdentityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var candidate = new InstanceOperatorIdentitySettings
        {
            PublicName = request.PublicName,
            LegalName = request.LegalName,
            OperatorKindCode = request.OperatorKindCode,
            JurisdictionCountryCode = request.JurisdictionCountryCode,
            RegistrationIdentifier = request.RegistrationIdentifier,
            PublicContactEmail = request.PublicContactEmail,
            WebsiteUrl = request.WebsiteUrl,
            LegalNoticeUrl = request.LegalNoticeUrl,
            TermsUrl = request.TermsUrl,
            PrivacyUrl = request.PrivacyUrl,
            OfficialOrigin = request.OfficialOrigin
        };

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result =
            await identityService.SaveAsync(candidate, request.ExpectedRevision, cancellationToken);

        if (result.IsSuccess)
        {
            var dto = new InstanceOperatorIdentitySavedDocumentDto
            {
                Revision = result.Id!.Revision,
                IsReady = result.Id.Readiness.IsReady,
                FailureCode = result.Id.Readiness.FailureCode,
                ReasonCodes = result.Id.Readiness.ReasonCodes,
                OperatorId = result.Id.Readiness.Identity?.OperatorId
            };
            return BaseCommandResponse.Success(dto, result.Message);
        }

        if (result.FailureCode is not null)
        {
            return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocumentDto>(
                result.FailureCode,
                result.Message);
        }

        return BaseCommandResponse.Validation<InstanceOperatorIdentitySavedDocumentDto>(
            result.Errors,
            result.Message);
    }
}
