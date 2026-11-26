using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain.Learning;
using FluentValidation.Results;

namespace Explore.Application.Features.Learning;

internal static class LearningCommandFailures
{
    internal static LearningMutationResult Validation(ValidationResult result) =>
        new(BaseCommandResponse.Validation<Guid>(result.Errors.Select(error => error.ErrorMessage)), null);

    // Only synchronous Domain transition refusals are expected business conflicts.
    // Preparation/authority failures and ambiguous physical settlements must escape.
    internal static bool IsDomainConflict(InvalidOperationException exception) =>
        exception.GetType() == typeof(InvalidOperationException)
        && exception.TargetSite?.DeclaringType?.Namespace == typeof(UserLearningState).Namespace;

    internal static LearningMutationResult Conflict(Guid id, string message) =>
        new(BaseCommandResponse.Conflict(id, message), null);

    internal static LearningMutationResult Conflict(ConcurrencyConflictException exception, Guid id) =>
        Conflict(id, exception.Message);
}
