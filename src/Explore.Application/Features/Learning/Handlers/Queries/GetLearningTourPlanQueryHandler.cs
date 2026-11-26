using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Queries;
using Explore.Application.Features.Learning.Validators;
using FluentValidation.Results;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Queries;

public sealed class GetLearningTourPlanQueryHandler(
    IHumanCommitScopePreparation humanScope,
    IHumanLearningContextReader reader) : IQueryHandler<GetLearningTourPlanQuery, LearningTourPlanDto>
{
    public async Task<LearningTourPlanDto> QueryAsync(
        GetLearningTourPlanQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await new GetLearningContextValidator().ValidateAsync(
            new GetLearningContextQuery(query.ContextKey, query.EventContextId), cancellationToken);
        if (!Enum.TryParse<LearningTourFamily>(query.Family, ignoreCase: false, out var family)
            || !Enum.IsDefined(family)
            || !string.Equals(Enum.GetName(family), query.Family, StringComparison.Ordinal))
            validation.Errors.Add(new ValidationFailure(nameof(query.Family),
                "Learning tour family must be an exact named value."));
        else if ((family == LearningTourFamily.EventManagement) != query.EventContextId.HasValue)
            validation.Errors.Add(new ValidationFailure(nameof(query.EventContextId),
                "Only the event-management tour requires an event context."));
        if (!validation.IsValid)
            throw new ValidationException(validation);

        await humanScope.PrepareAsync(cancellationToken);
        var operation = await reader.ReadContextAsync(query.ContextKey, query.EventContextId, cancellationToken);
        var sources = operation.Sources
            ?? throw new UnauthorizedAccessException("A learning plan requires its original retained source image.");
        return LearningTourPlanResolver.Resolve(operation, sources, family).Plan;
    }
}
