namespace ISLAMU.Event.Setup.Core;

public enum SetupWorkflowState
{
    Draft,
    Validated,
    Ready,
    Incomplete,
    Failed,
    Exported
}

public enum SetupWorkflowAction
{
    Validate,
    MarkReady,
    MarkIncomplete,
    Fail,
    Export,
    Revise
}

public sealed record SetupTransitionResult(
    SetupWorkflowState State,
    bool Succeeded,
    SetupDiagnostic? Diagnostic);

public static class SetupWorkflow
{
    private static readonly SetupDiagnostic InvalidTransition = new(
        new SetupDiagnosticCode("invalid-transition"),
        new SetupDiagnosticPath("$.workflow.state"),
        SetupDiagnosticSeverity.Error);

    public static SetupTransitionResult Transition(
        SetupWorkflowState state,
        SetupWorkflowAction action)
    {
        SetupWorkflowState? next = (state, action) switch
        {
            (SetupWorkflowState.Draft, SetupWorkflowAction.Validate) => SetupWorkflowState.Validated,
            (SetupWorkflowState.Validated, SetupWorkflowAction.MarkReady) => SetupWorkflowState.Ready,
            (SetupWorkflowState.Validated, SetupWorkflowAction.MarkIncomplete) => SetupWorkflowState.Incomplete,
            (SetupWorkflowState.Validated, SetupWorkflowAction.Fail) => SetupWorkflowState.Failed,
            (SetupWorkflowState.Ready, SetupWorkflowAction.Export) => SetupWorkflowState.Exported,
            (SetupWorkflowState.Ready, SetupWorkflowAction.Fail) => SetupWorkflowState.Failed,
            (SetupWorkflowState.Incomplete, SetupWorkflowAction.Revise) => SetupWorkflowState.Draft,
            (SetupWorkflowState.Failed, SetupWorkflowAction.Revise) => SetupWorkflowState.Draft,
            (SetupWorkflowState.Ready, SetupWorkflowAction.Revise) => SetupWorkflowState.Draft,
            (SetupWorkflowState.Exported, SetupWorkflowAction.Revise) => SetupWorkflowState.Draft,
            _ => null
        };

        return next is { } accepted
            ? new SetupTransitionResult(accepted, true, null)
            : new SetupTransitionResult(state, false, InvalidTransition);
    }
}
