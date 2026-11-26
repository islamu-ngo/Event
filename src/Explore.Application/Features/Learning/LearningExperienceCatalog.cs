using Explore.Domain.Learning;
using ISLAMU.Wire.Contracts.Learning;
using LearningTourFamily = Explore.Domain.Learning.LearningTourFamily;

namespace Explore.Application.Features.Learning;

/// <summary>
/// Source-owned tour semantics, separate from practice recipes. Definitions are
/// authored inventories; the current server resolver selects applicable steps.
/// Presentation order, copy and target layout do not establish completion.
/// </summary>
public static class LearningExperienceCatalog
{
    public const int MaterialVersion = 2;

    private static readonly IReadOnlyList<LearningTourStepDto> ShellSteps = Array.AsReadOnly<LearningTourStepDto>(
    [
        Step("shell.navigation", "shell.navigation",
            "Use the main navigation to find the areas available in this tenant."),
        Step("shell.studio", "shell.studio",
            "Studio brings together event management, attendees and available analytics."),
        Step("shell.help", "shell.help",
            "Open Help when you want guidance or choose to replay a tour.")
    ]);

    private static readonly IReadOnlyList<LearningTourStepDto> StudioSteps = Array.AsReadOnly<LearningTourStepDto>(
    [
        Step("studio.actor-context", "studio.actor-context",
            "Check the current workspace context before managing its events."),
        Step("studio.event-list", "studio.event-list",
            "Find the events available to manage in the current workspace."),
        Step("studio.event-management", "studio.event-management",
            "Enter an event's management area to work with that event's available sections.")
    ]);

    private static readonly IReadOnlyList<LearningTourStepDto> EventManagementSteps = Array.AsReadOnly<LearningTourStepDto>(
    [
        Step("event-management.event-context", "event-management.event-context",
            "Check which event you are managing before making changes."),
        Step("event-management.return-to-studio", "event-management.return-to-studio",
            "Use All events to return to the current workspace's event list."),
        Step("event-management.sections", "event-management.sections",
            "Use the available event sections to reach the tools permitted in this context.")
    ]);

    private static readonly IReadOnlyList<LearningTourStepDto> NormalAiSteps = Array.AsReadOnly<LearningTourStepDto>(
    [
        Step("normal-ai.conversation", "normal-ai.conversation",
            "This is the normal AI conversation, shared across its workspace and panel."),
        Step("normal-ai.composer", "normal-ai.composer",
            "Use the composer when you choose to send a message to AI."),
        Step("normal-ai.response", "normal-ai.response",
            "Review AI responses before deciding what to do next.")
    ]);

    /// <summary>Returns authored metadata only; it does not grant access or resolve policy.</summary>
    public static IReadOnlyList<LearningTourStepDto> GetSteps(LearningTourFamily family) => family switch
    {
        LearningTourFamily.Shell => ShellSteps,
        LearningTourFamily.Studio => StudioSteps,
        LearningTourFamily.EventManagement => EventManagementSteps,
        LearningTourFamily.NormalAi => NormalAiSteps,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    /// <summary>
    /// Returns the full authored identity inventory. Resolve applicability before
    /// using a definition for current coverage or mutation admission.
    /// </summary>
    public static LearningTourDefinition GetDefinition(LearningTourFamily family) =>
        new(family, MaterialVersion, GetSteps(family)
            .Select(step => new LearningTourStep(step.StepKey, step.StepVersion)).ToArray());

    private static LearningTourStepDto Step(string stepKey, string targetKey, string instruction) => new()
    {
        StepKey = stepKey,
        StepVersion = 1,
        TargetKey = targetKey,
        Instruction = new LearningDisplayTextDto($"Learning.Tours.{stepKey}.instruction", instruction)
    };
}
