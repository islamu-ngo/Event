using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Validators;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Commands;

public sealed class UpdateLearningGuidancePolicyCommandHandler(IHumanLearningPolicyMutationPreparation preparation)
    : ICommandHandler<UpdateLearningGuidancePolicyCommand, LearningGuidancePolicyDto>
{
    public async Task<LearningGuidancePolicyDto> ExecuteAsync(
        UpdateLearningGuidancePolicyCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new UpdateLearningGuidancePolicyValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) throw new ValidationException(validation);
        var settled = await preparation.ExecuteAsync(command, cancellationToken);
        return LearningGuidancePolicyCodec.ToDto(settled, isCurrentTenantAdministrator: true);
    }
}
