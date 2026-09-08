using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Models;

internal sealed record CoopDecisionStageResult(
    BaseCommandResponse<Guid> Response,
    bool ShouldExecute,
    Guid DecisionId,
    Guid CaseConcurrencyStamp)
{
    public static CoopDecisionStageResult NoExecution(BaseCommandResponse<Guid> response) =>
        new(response, false, response.Id, Guid.Empty);

    public static CoopDecisionStageResult Execute(
        BaseCommandResponse<Guid> response,
        Guid decisionId,
        Guid caseConcurrencyStamp) =>
        new(response, true, decisionId, caseConcurrencyStamp);
}
