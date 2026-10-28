using System.Diagnostics.CodeAnalysis;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning;

/// <summary>
/// Original finite exercises. Numeric state reconstructs synthetic presentation;
/// no recipe resolves a service, invokes a callback or describes a production effect.
/// </summary>
public static class LearningLessonCatalog
{
    public const string PackKey = "learning-core";
    public const int MaterialVersion = 1;

    public static IReadOnlyList<LearningLessonDto> Lessons { get; } = Array.AsReadOnly<LearningLessonDto>(
    [
        Define("event-details", "Shape an example event",
            "Compare a room-based meetup with an online meetup using fictional details.",
            "Choose one format, inspect the example, then confirm the simulation.",
            "Learning access lets you explore formats without permission to create an event.",
            Choice("event-details", "in-person", LearningPracticeChoiceSlot.Primary, 1,
                "Meet in a room", "A room-based format needs a venue description and arrival information.",
                "preview-in-person", "Room-based example",
                "The fictional Community Skills Meetup uses the Practice Room. No venue or event was created.",
                new("format", "in-person"), new("venue", "practice-room"), new("event-name", "community-skills-meetup")),
            Choice("event-details", "online", LearningPracticeChoiceSlot.Secondary, 2,
                "Meet online", "An online format needs joining guidance rather than a physical room.",
                "preview-online", "Online example",
                "The fictional Community Skills Meetup shows a joining-information label, not a live meeting link.",
                new("format", "online"), new("venue", "practice-joining-label"), new("event-name", "community-skills-meetup"))),
        Define("sessions-program", "Build an example programme",
            "Compare one focused session with two sequential sessions in a fictional programme.",
            "Select a programme shape and confirm to see its synthetic duration.",
            "Learning access permits programme practice; it does not grant session-editing authority.",
            Choice("sessions-program", "single-session", LearningPracticeChoiceSlot.Primary, 1,
                "One focused session", "One 45-minute session keeps the example programme compact.",
                "preview-single-session", "One-session example",
                "The example contains a 45-minute Welcome and Practice session. Nothing was scheduled.",
                new("session-count", "1"), new("duration-minutes", "45"), new("schedule-state", "synthetic")),
            Choice("sessions-program", "two-sessions", LearningPracticeChoiceSlot.Secondary, 2,
                "Two sequential sessions", "Two 45-minute sessions illustrate a longer programme without overlap.",
                "preview-two-sessions", "Two-session example",
                "The example contains Welcome and Practice followed by Reflection, each 45 minutes. Nothing was scheduled.",
                new("session-count", "2"), new("duration-minutes", "90"), new("schedule-state", "synthetic"))),
        Define("ticket-capacity", "Explore example capacity",
            "Compare twelve and twenty-four places against eighteen fictional requests.",
            "Select a capacity, then confirm to inspect accepted and waitlisted counts.",
            "Learning access permits capacity arithmetic without sales, payments or ticket-management authority.",
            Choice("ticket-capacity", "capacity-12", LearningPracticeChoiceSlot.Primary, 12,
                "Twelve places", "With eighteen fictional requests, twelve fit and six remain on the example waitlist.",
                "preview-capacity-12", "Limited-capacity example",
                "This calculation reserves no places, creates no tickets and charges nobody.",
                new("capacity", "12"), new("requested-count", "18"), new("accepted-count", "12"), new("waitlisted-count", "6")),
            Choice("ticket-capacity", "capacity-24", LearningPracticeChoiceSlot.Secondary, 24,
                "Twenty-four places", "All eighteen fictional requests fit, leaving six unused places.",
                "preview-capacity-24", "Spare-capacity example",
                "This calculation reserves no places, creates no tickets and charges nobody.",
                new("capacity", "24"), new("requested-count", "18"), new("accepted-count", "18"), new("waitlisted-count", "0"))),
        Define("attendees-team", "Compare example participation roles",
            "Use the fictional Sample Learner to distinguish attending from helping.",
            "Choose one example role, then confirm to view a role preview.",
            "Learning access permits a role preview, not invitations, membership changes or attendee access.",
            Choice("attendees-team", "attendee", LearningPracticeChoiceSlot.Primary, 1,
                "Attend the example", "An attendee takes part without a team responsibility in this exercise.",
                "preview-attendee", "Attendee example",
                "Sample Learner appears as a fictional attendee. No person was invited or registered.",
                new("person", "sample-learner"), new("role", "attendee"), new("invitation-state", "not-sent")),
            Choice("attendees-team", "helper", LearningPracticeChoiceSlot.Secondary, 2,
                "Help with the example", "A helper has an example welcome-desk responsibility, not real permissions.",
                "preview-helper", "Helper example",
                "Sample Learner appears at the fictional welcome desk. No team membership or invitation was created.",
                new("person", "sample-learner"), new("role", "helper"), new("invitation-state", "not-sent"))),
        Define("publication-lifecycle", "Inspect an example publication decision",
            "Compare keeping a fictional draft with previewing how publication would look.",
            "Choose draft or publication preview and confirm the simulation; neither choice publishes.",
            "Learning access permits this preview without publication rights. Real publication requires a separately authorized workflow.",
            Choice("publication-lifecycle", "keep-draft", LearningPracticeChoiceSlot.Primary, 0,
                "Keep the example draft", "A draft remains a private example while its details are reviewed.",
                "preview-draft", "Draft example",
                "The example remains a draft in this simulation. No production draft was created or changed.",
                new("lifecycle", "draft"), new("visibility", "private"), new("published", "false")),
            Choice("publication-lifecycle", "preview-publication", LearningPracticeChoiceSlot.Secondary, 1,
                "Preview publication", "Inspect a publication-shaped preview without changing public visibility.",
                "preview-publication", "Publication preview only",
                "This is a synthetic publication preview. Nothing was published, indexed, announced or federated.",
                new("lifecycle", "publication-preview"), new("visibility", "private"), new("published", "false"))),
        Define("ai-example-review", "Review an authored AI-shaped example",
            "Review a prewritten suggestion: add a short welcome session before the fictional practice session.",
            "Accept or reject the authored example, then confirm to inspect the simulated review result.",
            "Learning access permits reviewing this fixed example. It does not invoke an AI provider or authorize a real action.",
            Choice("ai-example-review", "accept-example", LearningPracticeChoiceSlot.Primary, 1,
                "Accept the example suggestion", "Acceptance changes only the exercise's review state.",
                "example-accepted", "Example accepted in simulation",
                "The fixed suggestion was accepted for this exercise. No AI request, conversation or real draft exists.",
                new("review", "accepted"), new("draft-state", "synthetic-only"), new("provider-contacted", "false")),
            Choice("ai-example-review", "reject-example", LearningPracticeChoiceSlot.Secondary, 0,
                "Reject the example suggestion", "Rejection leaves the fictional programme unchanged.",
                "example-rejected", "Example rejected in simulation",
                "The fixed suggestion was rejected for this exercise. No AI request, conversation or real draft exists.",
                new("review", "rejected"), new("draft-state", "synthetic-only"), new("provider-contacted", "false")))
    ]);

    public static bool TryGetLesson(
        string recipeKey, int materialVersion, [NotNullWhen(true)] out LearningLessonDto? lesson)
    {
        lesson = materialVersion == MaterialVersion
            ? Lessons.SingleOrDefault(item => string.Equals(item.RecipeKey, recipeKey, StringComparison.Ordinal))
            : null;
        return lesson is not null;
    }

    /// <summary>
    /// Restores only canonical numeric storage states. Cleared practices are deleted by
    /// the owning mutation; their receipts do not become persisted arbitrary payloads.
    /// </summary>
    public static bool TryRestore(
        string recipeKey,
        int materialVersion,
        LearningPracticeChoiceSlot choiceSlot,
        int step,
        int value,
        bool isConfirmed,
        [NotNullWhen(true)] out LearningSimulationStateDto? state)
    {
        state = null;
        if (!TryGetLesson(recipeKey, materialVersion, out LearningLessonDto? lesson))
            return false;

        if (choiceSlot == LearningPracticeChoiceSlot.None && step == 0 && value == 0 && !isConfirmed)
        {
            state = Present(lesson, "choose", null);
            return true;
        }

        LearningChoiceDto? choice = lesson.Choices.SingleOrDefault(item => item.Slot == choiceSlot);
        if (choice is null || choice.Value != value
            || !((step == 1 && !isConfirmed) || (step == 2 && isConfirmed)))
            return false;

        state = Present(lesson, isConfirmed ? "result" : "confirm", choice);
        return true;
    }

    /// <summary>
    /// Evaluates an allow-listed transition and returns fresh authored presentation plus
    /// the finite ChoiceSlot/Step/Value consumed by Domain. Refusal always returns null.
    /// </summary>
    public static bool TryApply(
        string recipeKey,
        int materialVersion,
        LearningPracticeAction action,
        string? choiceKey,
        LearningSimulationStateDto? current,
        [NotNullWhen(true)] out LearningSimulationStateDto? next)
    {
        next = null;
        if (!TryGetLesson(recipeKey, materialVersion, out LearningLessonDto? lesson))
            return false;

        string fromStep = "absent";
        LearningChoiceDto? selected = null;
        if (current is not null)
        {
            if (current.RecipeKey != recipeKey || current.MaterialVersion != materialVersion)
                return false;

            bool cleared = current.Status == LearningSimulationStatus.Cleared
                && current.StepKey == "cleared" && current.ChoiceSlot == LearningPracticeChoiceSlot.None
                && current.Step == 0 && current.Value == 0 && !current.IsConfirmed && current.ChoiceKey is null;
            if (cleared)
            {
                fromStep = "cleared";
            }
            else
            {
                if (!TryRestore(recipeKey, materialVersion, current.ChoiceSlot, current.Step,
                        current.Value, current.IsConfirmed, out LearningSimulationStateDto? restored)
                    || restored.Status != current.Status || restored.StepKey != current.StepKey
                    || restored.ChoiceKey != current.ChoiceKey)
                    return false;
                fromStep = restored.StepKey;
                selected = lesson.Choices.SingleOrDefault(item => item.Key == restored.ChoiceKey);
            }
        }

        LearningRecipeTransitionDto? transition = lesson.Transitions.SingleOrDefault(item =>
            item.FromStepKey == fromStep && item.Action == action
            && string.Equals(item.ChoiceKey, choiceKey, StringComparison.Ordinal));
        if (transition is null)
            return false;

        if (action == LearningPracticeAction.Choose)
            selected = lesson.Choices.Single(item => item.Key == choiceKey);
        if (transition.ToStepKey is "choose" or "cleared")
            selected = null;

        next = Present(lesson, transition.ToStepKey, selected);
        return true;
    }

    private static LearningSimulationStateDto Present(
        LearningLessonDto lesson, string stepKey, LearningChoiceDto? choice)
    {
        bool confirmed = stepKey == "result";
        return new()
        {
            RecipeKey = lesson.RecipeKey,
            MaterialVersion = MaterialVersion,
            StepKey = stepKey,
            ChoiceKey = choice?.Key,
            ChoiceSlot = choice?.Slot ?? LearningPracticeChoiceSlot.None,
            Step = stepKey switch { "confirm" => 1, "result" => 2, _ => 0 },
            Value = choice?.Value ?? 0,
            IsConfirmed = confirmed,
            Status = stepKey switch
            {
                "choose" => LearningSimulationStatus.Choosing,
                "confirm" => LearningSimulationStatus.AwaitingConfirmation,
                "result" => LearningSimulationStatus.Simulated,
                "cleared" => LearningSimulationStatus.Cleared,
                _ => throw new InvalidOperationException("Unknown authored learning step.")
            },
            Presentation = choice?.Result ?? new()
            {
                ScenarioKey = lesson.RecipeKey,
                Heading = lesson.Title,
                Description = stepKey == "cleared"
                    ? Text(lesson.RecipeKey, "cleared", "The exercise was cleared. No production action occurred.")
                    : lesson.Description
            },
            Receipt = confirmed ? new()
            {
                RecipeKey = lesson.RecipeKey,
                MaterialVersion = MaterialVersion,
                ChoiceKey = choice!.Key,
                ResultKey = choice.ResultKey
            } : null
        };
    }

    private static LearningLessonDto Define(
        string key, string title, string description, string prerequisite,
        string capabilityExplanation, params LearningChoiceDto[] choices)
    {
        List<LearningRecipeTransitionDto> transitions =
        [
            new("absent", LearningPracticeAction.Start, null, "choose"),
            new("cleared", LearningPracticeAction.Start, null, "choose"),
            new("confirm", LearningPracticeAction.Confirm, null, "result"),
            new("choose", LearningPracticeAction.Reset, null, "choose"),
            new("confirm", LearningPracticeAction.Reset, null, "choose"),
            new("result", LearningPracticeAction.Reset, null, "choose"),
            new("choose", LearningPracticeAction.Clear, null, "cleared"),
            new("confirm", LearningPracticeAction.Clear, null, "cleared"),
            new("result", LearningPracticeAction.Clear, null, "cleared")
        ];
        foreach (LearningChoiceDto choice in choices)
        {
            transitions.Add(new("choose", LearningPracticeAction.Choose, choice.Key, "confirm"));
            transitions.Add(new("confirm", LearningPracticeAction.Choose, choice.Key, "confirm"));
        }
        return new()
        {
            PackKey = PackKey,
            MaterialVersion = MaterialVersion,
            RecipeKey = key,
            Title = Text(key, "title", title),
            Description = Text(key, "description", description),
            RequiredCapabilityKey = "learning.simulate",
            CapabilityExplanation = Text(key, "capability", capabilityExplanation),
            Prerequisite = Text(key, "prerequisite", prerequisite),
            Choices = choices,
            Transitions = transitions
        };
    }

    private static LearningChoiceDto Choice(
        string recipe, string key, LearningPracticeChoiceSlot slot, int value,
        string label, string explanation, string resultKey, string heading,
        string resultDescription, params LearningSyntheticFactDto[] facts) => new()
    {
        Key = key,
        Slot = slot,
        Value = value,
        Label = Text(recipe, $"choices.{key}.label", label),
        Explanation = Text(recipe, $"choices.{key}.explanation", explanation),
        ResultKey = resultKey,
        Result = new()
        {
            ScenarioKey = recipe,
            Heading = Text(recipe, $"results.{resultKey}.heading", heading),
            Description = Text(recipe, $"results.{resultKey}.description", resultDescription),
            Facts = facts
        }
    };

    private static LearningDisplayTextDto Text(string recipe, string key, string fallback) =>
        new($"Learning.{PackKey}.{recipe}.{key}", fallback);
}
