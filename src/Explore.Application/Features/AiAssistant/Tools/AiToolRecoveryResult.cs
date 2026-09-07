namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolRecoveryResult(
    bool RequiresClarification,
    string? ClarificationQuestion,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NextActions,
    string? StableFailureCode,
    string? MachineOutputJson)
{
    public const int MaxMachineOutputJsonLength = 1000;

    public static AiToolRecoveryResult None { get; } = new(false, null, [], [], null, null);

    public static AiToolRecoveryResult ForFailure(
        string stableFailureCode,
        string? nextAction = null,
        string? machineOutputJson = null)
        => new(
            RequiresClarification: false,
            ClarificationQuestion: null,
            Warnings: [],
            NextActions: BuildList(nextAction),
            StableFailureCode: stableFailureCode,
            MachineOutputJson: BoundMachineOutput(machineOutputJson));

    public static AiToolRecoveryResult ForClarification(
        string stableFailureCode,
        string clarificationQuestion,
        string? nextAction = null,
        string? machineOutputJson = null)
        => new(
            RequiresClarification: true,
            ClarificationQuestion: string.IsNullOrWhiteSpace(clarificationQuestion)
                ? "Please provide the missing information before this action can be proposed."
                : clarificationQuestion.Trim(),
            Warnings: [],
            NextActions: BuildList(nextAction),
            StableFailureCode: stableFailureCode,
            MachineOutputJson: BoundMachineOutput(machineOutputJson));

    public static AiToolRecoveryResult WithWarnings(
        IReadOnlyList<string> warnings,
        IReadOnlyList<string>? nextActions = null)
        => new(
            RequiresClarification: false,
            ClarificationQuestion: null,
            Warnings: NormalizeList(warnings),
            NextActions: NormalizeList(nextActions ?? []),
            StableFailureCode: null,
            MachineOutputJson: null);

    private static IReadOnlyList<string> BuildList(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Length <= 240)
            .ToList();

    private static string? BoundMachineOutput(string? machineOutputJson)
    {
        if (string.IsNullOrWhiteSpace(machineOutputJson))
        {
            return null;
        }

        var trimmed = machineOutputJson.Trim();
        return trimmed.Length <= MaxMachineOutputJsonLength ? trimmed : null;
    }
}
