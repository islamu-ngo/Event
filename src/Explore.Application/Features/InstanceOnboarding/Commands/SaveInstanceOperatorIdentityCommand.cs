namespace Explore.Application.Features.InstanceOnboarding.Commands;

using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using Explore.Application.Services;
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

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result =
            await identityService.SaveAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            var dto = new InstanceOperatorIdentitySavedDocumentDto
            {
                Revision = result.Id!.Revision,
                PublicDisclosure = new(result.Id.PublicDisclosure.IsReady, result.Id.PublicDisclosure.FailureCode, result.Id.PublicDisclosure.ReasonCodes),
                PaidCommerce = new(result.Id.PaidCommerce.IsReady, result.Id.PaidCommerce.FailureCode, result.Id.PaidCommerce.ReasonCodes),
                OperatorId = result.Id.OperatorId
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
