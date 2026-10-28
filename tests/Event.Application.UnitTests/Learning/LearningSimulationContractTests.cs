using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Features.Learning;
using ISLAMU.Wire.Contracts.Learning;

namespace Event.Application.UnitTests.Learning;

public sealed class LearningSimulationContractTests
{
    [Test]
    [Arguments("event-details", "in-person", "preview-in-person", "format", "in-person")]
    [Arguments("event-details", "online", "preview-online", "format", "online")]
    [Arguments("sessions-program", "single-session", "preview-single-session", "session-count", "1")]
    [Arguments("sessions-program", "two-sessions", "preview-two-sessions", "session-count", "2")]
    [Arguments("ticket-capacity", "capacity-12", "preview-capacity-12", "waitlisted-count", "6")]
    [Arguments("ticket-capacity", "capacity-24", "preview-capacity-24", "waitlisted-count", "0")]
    [Arguments("attendees-team", "attendee", "preview-attendee", "role", "attendee")]
    [Arguments("attendees-team", "helper", "preview-helper", "role", "helper")]
    [Arguments("publication-lifecycle", "keep-draft", "preview-draft", "lifecycle", "draft")]
    [Arguments("publication-lifecycle", "preview-publication", "preview-publication", "lifecycle", "publication-preview")]
    [Arguments("ai-example-review", "accept-example", "example-accepted", "review", "accepted")]
    [Arguments("ai-example-review", "reject-example", "example-rejected", "review", "rejected")]
    public async Task FiniteChoices_ProduceOnlyDeterministicSimulatedReceipts(
        string recipe, string choice, string result, string fact, string value)
    {
        LearningSimulationStateDto started = Apply(recipe, LearningPracticeAction.Start);
        await Assert.That(started.Status).IsEqualTo(LearningSimulationStatus.Choosing);
        await Assert.That(started.Receipt is null).IsTrue();

        LearningSimulationStateDto chosen = Apply(recipe, LearningPracticeAction.Choose, started, choice);
        await Assert.That(chosen.Status).IsEqualTo(LearningSimulationStatus.AwaitingConfirmation);
        await Assert.That(chosen.Receipt is null).IsTrue();
        await Assert.That(chosen.Step).IsEqualTo(1);
        await Assert.That(chosen.IsConfirmed).IsFalse();

        LearningSimulationStateDto confirmed = Apply(recipe, LearningPracticeAction.Confirm, chosen);
        LearningSimulationStateDto repeated = Apply(recipe, LearningPracticeAction.Confirm, chosen);
        await Assert.That(confirmed.Status).IsEqualTo(LearningSimulationStatus.Simulated);
        await Assert.That(confirmed.Receipt!.ResultKey).IsEqualTo(result);
        await Assert.That(confirmed.Receipt.ChoiceKey).IsEqualTo(choice);
        await Assert.That(confirmed.Receipt.IsSimulation).IsTrue();
        await Assert.That(confirmed.Receipt.ProductionEffects).IsFalse();
        await Assert.That(confirmed.Step).IsEqualTo(2);
        await Assert.That(confirmed.IsConfirmed).IsTrue();
        await Assert.That(confirmed.ChoiceSlot == LearningPracticeChoiceSlot.Primary
            || confirmed.ChoiceSlot == LearningPracticeChoiceSlot.Secondary).IsTrue();
        await Assert.That(confirmed.Presentation.Facts.Single(item => item.Key == fact).Value).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(confirmed)).IsEqualTo(JsonSerializer.Serialize(repeated));
    }

    [Test]
    [Arguments("event-details")]
    [Arguments("sessions-program")]
    [Arguments("ticket-capacity")]
    [Arguments("attendees-team")]
    [Arguments("publication-lifecycle")]
    [Arguments("ai-example-review")]
    public async Task FiniteRecipes_RefuseUnsupportedInputsAndIllegalOrdering(string recipe)
    {
        LearningSimulationStateDto started = Apply(recipe, LearningPracticeAction.Start);
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, null);
        RequireRefused(recipe, 1, LearningPracticeAction.Choose, null, "online");
        RequireRefused(recipe, 1, LearningPracticeAction.Start, started);
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, started);
        RequireRefused(recipe, 1, LearningPracticeAction.Choose, started, "production-publish");
        RequireRefused(recipe, 1, LearningPracticeAction.Choose, started, "");
        RequireRefused(recipe, 1, LearningPracticeAction.Choose, started, null);
        RequireRefused(recipe, 1, LearningPracticeAction.Start, null, "unexpected");
        RequireRefused(recipe, 1, (LearningPracticeAction)999, started);
        await Assert.That(started.Status).IsEqualTo(LearningSimulationStatus.Choosing);
        await Assert.That(started.ChoiceKey is null).IsTrue();
    }

    [Test]
    [Arguments("event-details")]
    [Arguments("sessions-program")]
    [Arguments("ticket-capacity")]
    [Arguments("attendees-team")]
    [Arguments("publication-lifecycle")]
    [Arguments("ai-example-review")]
    public async Task ResetAndClear_DiscardChoicesAndReceiptsWithoutChangingRecipe(string recipe)
    {
        LearningLessonDto lesson = LearningLessonCatalog.Lessons.Single(item => item.RecipeKey == recipe);
        LearningSimulationStateDto started = Apply(recipe, LearningPracticeAction.Start);
        LearningSimulationStateDto chosen = Apply(recipe, LearningPracticeAction.Choose, started, lesson.Choices[0].Key);
        LearningSimulationStateDto result = Apply(recipe, LearningPracticeAction.Confirm, chosen);
        LearningSimulationStateDto reset = Apply(recipe, LearningPracticeAction.Reset, result);
        await Assert.That(reset.Status).IsEqualTo(LearningSimulationStatus.Choosing);
        await Assert.That(reset.ChoiceKey is null && reset.Receipt is null).IsTrue();
        await Assert.That(reset.RecipeKey).IsEqualTo(recipe);

        LearningSimulationStateDto cleared = Apply(recipe, LearningPracticeAction.Clear, chosen);
        await Assert.That(cleared.Status).IsEqualTo(LearningSimulationStatus.Cleared);
        await Assert.That(cleared.ChoiceKey is null && cleared.Receipt is null).IsTrue();
        await Assert.That(cleared.Presentation.Facts.Count).IsEqualTo(0);
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, cleared);
        RequireRefused(recipe, 1, LearningPracticeAction.Reset, cleared);
        await Assert.That(Apply(recipe, LearningPracticeAction.Start, cleared).Status)
            .IsEqualTo(LearningSimulationStatus.Choosing);
    }

    [Test]
    [Arguments("event-details")]
    [Arguments("sessions-program")]
    [Arguments("ticket-capacity")]
    [Arguments("attendees-team")]
    [Arguments("publication-lifecycle")]
    [Arguments("ai-example-review")]
    public async Task ChangedChoices_UseLatestAllowedSelectionAndRejectForeignState(string recipe)
    {
        LearningLessonDto lesson = LearningLessonCatalog.Lessons.Single(item => item.RecipeKey == recipe);
        LearningSimulationStateDto started = Apply(recipe, LearningPracticeAction.Start);
        LearningSimulationStateDto first = Apply(recipe, LearningPracticeAction.Choose, started, lesson.Choices[0].Key);
        LearningSimulationStateDto second = Apply(recipe, LearningPracticeAction.Choose, first, lesson.Choices[1].Key);
        await Assert.That(Apply(recipe, LearningPracticeAction.Confirm, second).Receipt!.ChoiceKey)
            .IsEqualTo(lesson.Choices[1].Key);
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, second with { RecipeKey = "foreign" });
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, second with { MaterialVersion = 2 });
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, second with { StepKey = "result" });
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, second with { ChoiceKey = "unsupported" });
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, second, lesson.Choices[0].Key);
    }

    [Test]
    [Arguments("missing", 1)]
    [Arguments("EVENT-DETAILS", 1)]
    [Arguments("event-details", 0)]
    [Arguments("event-details", 2)]
    public async Task UnsupportedRecipeOrMaterial_IsRefused(string recipe, int version)
    {
        await Assert.That(LearningLessonCatalog.TryGetLesson(recipe, version, out _)).IsFalse();
        RequireRefused(recipe, version, LearningPracticeAction.Start, null);
    }

    [Test]
    public async Task Catalogue_DeclaresExactlySixExecutableClosedRecipes()
    {
        string[] expected =
        [
            "event-details", "sessions-program", "ticket-capacity",
            "attendees-team", "publication-lifecycle", "ai-example-review"
        ];
        await Assert.That(LearningLessonCatalog.PackKey).IsEqualTo("learning-core");
        await Assert.That(LearningLessonCatalog.MaterialVersion).IsEqualTo(1);
        await Assert.That(LearningLessonCatalog.Lessons.Select(item => item.RecipeKey).SequenceEqual(expected)).IsTrue();
        foreach (LearningLessonDto lesson in LearningLessonCatalog.Lessons)
        {
            await Assert.That(lesson.Choices.Count).IsEqualTo(2);
            await Assert.That(lesson.RequiredCapabilityKey.Length > 0).IsTrue();
            await Assert.That(lesson.Prerequisite.Key.Length > 0 && lesson.Prerequisite.Fallback.Length > 0).IsTrue();
            await Assert.That(lesson.CapabilityExplanation.Key.Length > 0 && lesson.CapabilityExplanation.Fallback.Length > 0).IsTrue();
            foreach (LearningRecipeTransitionDto transition in lesson.Transitions)
            {
                LearningSimulationStateDto? current = transition.FromStepKey switch
                {
                    "absent" => null,
                    "choose" => Apply(lesson.RecipeKey, LearningPracticeAction.Start),
                    "confirm" => Apply(lesson.RecipeKey, LearningPracticeAction.Choose,
                        Apply(lesson.RecipeKey, LearningPracticeAction.Start), lesson.Choices[0].Key),
                    "result" => Apply(lesson.RecipeKey, LearningPracticeAction.Confirm,
                        Apply(lesson.RecipeKey, LearningPracticeAction.Choose,
                            Apply(lesson.RecipeKey, LearningPracticeAction.Start), lesson.Choices[0].Key)),
                    "cleared" => Apply(lesson.RecipeKey, LearningPracticeAction.Clear,
                        Apply(lesson.RecipeKey, LearningPracticeAction.Start)),
                    _ => throw new InvalidOperationException("Unknown declared transition source.")
                };
                LearningSimulationStateDto next = Apply(lesson.RecipeKey, transition.Action, current, transition.ChoiceKey);
                await Assert.That(next.StepKey).IsEqualTo(transition.ToStepKey);
            }
        }
    }

    [Test]
    public async Task WireEnums_UseOnlyExactNamedStringTokens()
    {
        AssertClosedEnum<LearningTourFamily>();
        AssertClosedEnum<LearningTourOutcome>();
        AssertClosedEnum<LearningPracticeAction>();
        AssertClosedEnum<LearningSpaceAction>();
        AssertClosedEnum<LearningSpaceStatus>();
        AssertClosedEnum<LearningSimulationStatus>();
        AssertClosedEnum<LearningPracticeChoiceSlot>();
        await Assert.That(JsonSerializer.Serialize(LearningTourFamily.NormalAi)).IsEqualTo("\"NormalAi\"");
    }

    [Test]
    [Arguments("event-details", "online", 2)]
    [Arguments("sessions-program", "two-sessions", 2)]
    [Arguments("ticket-capacity", "capacity-12", 12)]
    [Arguments("attendees-team", "helper", 2)]
    [Arguments("publication-lifecycle", "keep-draft", 0)]
    [Arguments("ai-example-review", "reject-example", 0)]
    public async Task NativeSlots_RestoreOnlyCanonicalFiniteMachineState(string recipe, string choice, int value)
    {
        LearningSimulationStateDto chosen = Apply(recipe, LearningPracticeAction.Choose,
            Apply(recipe, LearningPracticeAction.Start), choice);
        await Assert.That(chosen.Value).IsEqualTo(value);
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, chosen.ChoiceSlot, 1, value, false, out var restored))
            .IsTrue();
        await Assert.That(restored!.ChoiceKey).IsEqualTo(choice);
        await Assert.That(Apply(recipe, LearningPracticeAction.Confirm, restored).IsConfirmed).IsTrue();
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, chosen.ChoiceSlot, 2, value, true, out var result))
            .IsTrue();
        await Assert.That(result!.Receipt!.ChoiceKey).IsEqualTo(choice);
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, LearningPracticeChoiceSlot.Tertiary, 1, value, false, out _))
            .IsFalse();
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, chosen.ChoiceSlot, 1, 999, false, out _)).IsFalse();
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, chosen.ChoiceSlot, 2, value, false, out _)).IsFalse();
        await Assert.That(LearningLessonCatalog.TryRestore(recipe, 1, chosen.ChoiceSlot, 1, value, true, out _)).IsFalse();
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, chosen with { Value = 999 });
        RequireRefused(recipe, 1, LearningPracticeAction.Confirm, chosen with { Step = 2 });
    }

    [Test]
    public async Task WireInputs_RejectUnknownOwnershipEffectsAndNumericActions()
    {
        RejectJson<LearningApplyPracticeActionRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"IntentId":"019f0e00-0000-7000-8000-000000000001","ExpectedPracticeRevision":0,"RecipeKey":"event-details","MaterialVersion":1,"ContextKey":"self","Action":0}""");
        RejectJson<LearningApplyPracticeActionRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"IntentId":"019f0e00-0000-7000-8000-000000000001","ExpectedPracticeRevision":0,"RecipeKey":"event-details","MaterialVersion":1,"ContextKey":"self","Action":"Start","EventId":"00000000-0000-0000-0000-000000000001"}""");
        RejectJson<LearningUpdatePreferencesRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"SuppressAutomaticTours":false,"HideLearning":false,"PinLearning":false,"UserId":"foreign"}""");
        RejectJson<LearningClaimTourRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"IntentId":"019f0e00-0000-7000-8000-000000000001","Family":"Shell","MaterialVersion":1,"ContextKey":"self","EffectUrl":"/publish"}""");
        RejectJson<LearningRecordTourOutcomeRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"IntentId":"019f0e00-0000-7000-8000-000000000001","Family":"Shell","MaterialVersion":1,"Outcome":"Unknown","StepIndex":0}""");
        RejectJson<LearningManageSpaceRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"Action":"Remove","TenantId":"foreign"}""");
        await Assert.That(JsonSerializer.Deserialize<LearningApplyPracticeActionRequest>(
            """{"ExpectedScopeStamp":"scope","ExpectedRevision":0,"IntentId":"019f0e00-0000-7000-8000-000000000001","ExpectedPracticeRevision":0,"RecipeKey":"event-details","MaterialVersion":1,"ContextKey":"self","Action":"Start"}""")!.Action)
            .IsEqualTo(LearningPracticeAction.Start);
    }

    [Test]
    public async Task PublishedCollections_SnapshotInputsAndRoundTripJson()
    {
        var choices = LearningLessonCatalog.Lessons[0].Choices.ToList();
        var transitions = LearningLessonCatalog.Lessons[0].Transitions.ToList();
        LearningLessonDto lesson = LearningLessonCatalog.Lessons[0] with { Choices = choices, Transitions = transitions };
        choices.Clear();
        transitions.Clear();
        await Assert.That(lesson.Choices.Count).IsEqualTo(2);
        await Assert.That(lesson.Transitions.Count > 0).IsTrue();
        AssertReadOnly(lesson.Choices);
        AssertReadOnly(lesson.Transitions);

        var lessons = new List<LearningLessonDto> { lesson };
        var tours = new List<LearningTourProgressDto>
        {
            new() { Family = LearningTourFamily.Shell, MaterialVersion = 1, StepIndex = 0 }
        };
        LearningContextDto context = Context() with { Lessons = lessons, Tours = tours };
        lessons.Clear();
        tours.Clear();
        await Assert.That(context.Lessons.Count).IsEqualTo(1);
        await Assert.That(context.Tours.Count).IsEqualTo(1);
        AssertReadOnly(context.Lessons);
        AssertReadOnly(context.Tours);

        var facts = new List<LearningSyntheticFactDto> { new("capacity", "12") };
        LearningSyntheticPresentationDto presentation = Apply("event-details", LearningPracticeAction.Start).Presentation
            with { Facts = facts };
        facts.Clear();
        await Assert.That(presentation.Facts.Single().Value).IsEqualTo("12");
        AssertReadOnly(presentation.Facts);

        LearningContextDto rebound = JsonSerializer.Deserialize<LearningContextDto>(JsonSerializer.Serialize(context))!;
        await Assert.That(rebound.Lessons[0].Choices.Count).IsEqualTo(2);
        await Assert.That(rebound.Tours[0].Family).IsEqualTo(LearningTourFamily.Shell);
        await Assert.That(rebound.LearnerRevision).IsEqualTo(0L);
        LearningPracticeDto practice = new()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ScopeStamp = "scope", LearnerRevision = 1,
            ContextKey = "self", RecipeKey = "event-details", MaterialVersion = 1,
            Revision = 1, Simulation = Apply("event-details", LearningPracticeAction.Start)
        };
        await Assert.That(JsonSerializer.Deserialize<LearningPracticeDto>(JsonSerializer.Serialize(practice))!.Simulation.StepKey)
            .IsEqualTo("choose");
    }

    [Test]
    public async Task HalExtensions_PreserveUnknownLinksAndIsolateWithCopies()
    {
        var links = new Dictionary<string, object> { ["self"] = new { href = "/api/learning/context" } };
        LearningContextDto original = Context() with
        {
            AdditionalProperties = ImmutableDictionary<string, object>.Empty.Add("_links", links)
        };
        links.Clear();
        LearningContextDto copy = original with
        {
            AdditionalProperties = ImmutableDictionary<string, object>.Empty.Add("_links", new { next = new { href = "/next" } })
        };
        using JsonDocument originalJson = JsonDocument.Parse(JsonSerializer.Serialize(original));
        await Assert.That(originalJson.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString())
            .IsEqualTo("/api/learning/context");
        LearningContextDto rebound = JsonSerializer.Deserialize<LearningContextDto>(JsonSerializer.Serialize(copy))!;
        await Assert.That(rebound.AdditionalProperties.ContainsKey("_links")).IsTrue();
        await Assert.That(original.AdditionalProperties.ContainsKey("_links")).IsTrue();
    }

    [Test]
    public async Task ClaimsAndMutationReceipts_RoundTripTypedAffectedResources()
    {
        LearningTourClaimDto claim = new()
        {
            Token = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Family = LearningTourFamily.Studio, MaterialVersion = 1,
            ExpiresAtUtc = new DateTimeOffset(2026, 10, 28, 12, 2, 0, TimeSpan.Zero),
            Revision = 3
        };
        LearningMutationReceiptDto receipt = new()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            ScopeStamp = "scope",
            ResourceRevision = 3, LearnerRevision = 3, Claim = claim, State = Context(),
            Space = new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Status = LearningSpaceStatus.Active,
                PackKey = "learning-core", MaterialVersion = 1, Revision = 1 },
            SimulationStatus = LearningSimulationStatus.Simulated
        };
        LearningMutationReceiptDto rebound = JsonSerializer.Deserialize<LearningMutationReceiptDto>(JsonSerializer.Serialize(receipt))!;
        await Assert.That(rebound.Claim!.Token).IsEqualTo(claim.Token);
        await Assert.That(rebound.Claim.ExpiresAtUtc).IsEqualTo(claim.ExpiresAtUtc);
        await Assert.That(rebound.Claim.Family).IsEqualTo(LearningTourFamily.Studio);
        await Assert.That(rebound.Claim.Revision).IsEqualTo(3L);
        await Assert.That(rebound.Space!.Status).IsEqualTo(LearningSpaceStatus.Active);
        await Assert.That(rebound.SimulationStatus).IsEqualTo(LearningSimulationStatus.Simulated);
    }

    [Test]
    public async Task PublishedCollections_RejectExceedingTheirFiniteBounds()
    {
        LearningTourProgressDto tour = new()
        {
            Family = LearningTourFamily.Shell, MaterialVersion = 1, StepIndex = 0
        };
        LearningLessonDto lesson = LearningLessonCatalog.Lessons[0];
        RequireBoundRefusal(() => Context() with { Tours = Enumerable.Repeat(tour, 9).ToArray() });
        RequireBoundRefusal(() => Context() with { Lessons = Enumerable.Repeat(lesson, 7).ToArray() });
        RequireBoundRefusal(() => lesson with { Choices = Enumerable.Repeat(lesson.Choices[0], 4).ToArray() });
        RequireBoundRefusal(() => lesson with { Transitions = Enumerable.Repeat(lesson.Transitions[0], 33).ToArray() });
        RequireBoundRefusal(() => lesson.Choices[0].Result with
        {
            Facts = Enumerable.Repeat(new LearningSyntheticFactDto("capacity", "12"), 9).ToArray()
        });
        await Assert.That((Context() with { Tours = Enumerable.Repeat(tour, 8).ToArray() }).Tours.Count).IsEqualTo(8);
    }

    private static LearningContextDto Context() => new()
    {
        TenantId = Guid.Parse("00000000-0000-0000-0000-000000000004"),
        UserId = Guid.Parse("00000000-0000-0000-0000-000000000005"),
        ScopeStamp = "scope", ResourceRevision = 0, LearnerRevision = 0,
        SpaceStatus = LearningSpaceStatus.Absent,
        Preferences = new(), Capabilities = new() { CanLearn = true }
    };

    private static LearningSimulationStateDto Apply(
        string recipe, LearningPracticeAction action, LearningSimulationStateDto? current = null, string? choice = null)
    {
        if (!LearningLessonCatalog.TryApply(recipe, 1, action, choice, current, out LearningSimulationStateDto? next)
            || next is null)
            throw new InvalidOperationException("Expected an executable synthetic transition.");
        return next;
    }

    private static void RequireRefused(
        string recipe, int version, LearningPracticeAction action, LearningSimulationStateDto? current, string? choice = null)
    {
        if (LearningLessonCatalog.TryApply(recipe, version, action, choice, current, out LearningSimulationStateDto? next)
            || next is not null)
            throw new InvalidOperationException("Unsupported simulation intent was accepted.");
    }

    private static void AssertClosedEnum<T>() where T : struct, Enum
    {
        foreach (T value in Enum.GetValues<T>())
        {
            if (!EqualityComparer<T>.Default.Equals(value, JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))))
                throw new InvalidOperationException("Closed enum failed its string round trip.");
        }
        foreach (string json in new[] { "0", "999", "\"0\"", "\"unknown\"", "null", "\"shell\"" })
            RejectJson<T>(json);
        try
        {
            JsonSerializer.Serialize((T)Enum.ToObject(typeof(T), 999));
        }
        catch (JsonException)
        {
            return;
        }
        throw new InvalidOperationException("Undefined enum value was serialized.");
    }

    private static void RejectJson<T>(string json)
    {
        try
        {
            JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid learning wire input was accepted.");
    }

    private static void AssertReadOnly(object collection)
    {
        if (collection is IList list && !list.IsReadOnly)
            throw new InvalidOperationException("Published collection exposes mutable storage.");
    }

    private static void RequireBoundRefusal(Func<object> create)
    {
        try
        {
            _ = create();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException("Unbounded learning payload was accepted.");
    }
}
