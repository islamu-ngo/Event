namespace Explore.Diagnostic.AiEvaluation;

public sealed record AiEvaluationScenarioResult(
    string Code,
    AiEvaluationDimension Dimension,
    AiEvaluationStatus Status,
    string Summary,
    string Recommendation)
{
    public static AiEvaluationScenarioResult Pass(
        string code,
        AiEvaluationDimension dimension,
        string summary,
        string recommendation)
        => new(code, dimension, AiEvaluationStatus.Pass, summary, recommendation);

    public static AiEvaluationScenarioResult Warn(
        string code,
        AiEvaluationDimension dimension,
        string summary,
        string recommendation)
        => new(code, dimension, AiEvaluationStatus.Warn, summary, recommendation);

    public static AiEvaluationScenarioResult Fail(
        string code,
        AiEvaluationDimension dimension,
        string summary,
        string recommendation)
        => new(code, dimension, AiEvaluationStatus.Fail, summary, recommendation);
}
