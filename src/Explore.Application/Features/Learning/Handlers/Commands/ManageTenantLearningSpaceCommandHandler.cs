using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Validators;

namespace Explore.Application.Features.Learning.Handlers.Commands;

public sealed class ManageTenantLearningSpaceCommandHandler(
    IHumanLearningMutationPreparation preparation) : ICommandHandler<ManageTenantLearningSpaceCommand, LearningMutationResult>
{
    public async Task<LearningMutationResult> ExecuteAsync(
        ManageTenantLearningSpaceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new ManageTenantLearningSpaceValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return LearningCommandFailures.Validation(validation);

        LearningOperationState operation;
        try
        {
            operation = await preparation.ExecuteAsync(command, cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            return LearningCommandFailures.Conflict(exception, Guid.Empty);
        }
        catch (InvalidOperationException exception) when (LearningCommandFailures.IsDomainConflict(exception))
        {
            return LearningCommandFailures.Conflict(Guid.Empty, exception.Message);
        }

        Guid id = operation.Space?.Id
            ?? throw new InvalidOperationException("A settled space mutation requires its space.");
        return LearningRepresentation.Mutation(operation, id, spaceMutation: true);
    }
}
