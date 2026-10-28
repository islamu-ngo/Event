using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Queries;
using Explore.Application.Features.Learning.Validators;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Queries;

public sealed class GetLearningContextQueryHandler(
    IHumanCommitScopePreparation humanScope,
    IHumanLearningContextReader reader) : IQueryHandler<GetLearningContextQuery, LearningContextDto>
{
    public async Task<LearningContextDto> QueryAsync(
        GetLearningContextQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await new GetLearningContextValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            throw new ValidationException(validation);

        await humanScope.PrepareAsync(cancellationToken);
        var operation = await reader.ReadContextAsync(query.ContextKey, query.EventContextId, cancellationToken);
        return LearningRepresentation.Context(operation);
    }
}
