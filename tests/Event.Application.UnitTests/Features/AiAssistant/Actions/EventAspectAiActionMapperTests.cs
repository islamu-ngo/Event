using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class EventAspectAiActionMapperTests
{
    [Test]
    public async Task MapIslamicUpsert_WhenPayloadIsValid_ReturnsGroupedUpdateCommand()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new UpsertEventIslamicAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "aspectKind": "islamic",
                "managementContextHasEdit": true,
                "madhabId": 1,
                "referencePrayer": 3,
                "prayerTimeOffset": 15,
                "genderMode": 4,
                "includesQuranRecitation": true,
                "primaryLanguageId": 2
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Command).IsNotNull();
        await Assert.That(result.Command!.EventId).IsEqualTo(eventId);
        await Assert.That(result.Command.AspectDto.PrayerSchedule!.ReferencePrayer.Value).IsEqualTo(PrayerTime.Dhuhr);
        await Assert.That(result.Command.AspectDto.Participation!.GenderMode).IsEqualTo(GenderSegregationMode.Family);
        await Assert.That(result.PermissionContext!.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.PermissionContext.AspectKind).IsEqualTo("islamic");
    }

    [Test]
    public async Task MapTechUpsert_WhenPayloadIsValid_ReturnsGroupedUpdateCommand()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new UpsertEventTechAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "aspectKind": "tech",
                "managementContextHasEdit": true,
                "githubRepoUrl": "https://github.com/islamu/event",
                "hackathonTrack": "  AI track  ",
                "skillLevel": 2,
                "techStackTags": ".NET, PostgreSQL",
                "requiresLaptop": true,
                "isCodingCompetition": true,
                "maxTeamSize": 4,
                "prizePool": 1000,
                "prizeCurrencyCode": "eur"
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Command).IsNotNull();
        await Assert.That(result.Command!.EventId).IsEqualTo(eventId);
        await Assert.That(result.Command.AspectDto.Classification!.SkillLevel).IsEqualTo(SkillLevel.Intermediate);
        await Assert.That(result.Command.AspectDto.Classification.HackathonTrack.Value).IsEqualTo("AI track");
        await Assert.That(result.Command.AspectDto.Prize!.PrizeCurrencyCode.Value).IsEqualTo("EUR");
        await Assert.That(result.PermissionContext!.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.PermissionContext.AspectKind).IsEqualTo("tech");
    }

    [Test]
    public async Task MapIslamicDelete_WhenPayloadIsConfirmed_ReturnsDeleteCommand()
    {
        var eventId = Guid.CreateVersion7();

        var result = new DeleteEventIslamicAspectAiActionMapper().Map(
            CreateDeletePayload(eventId, "islamic", "DELETE_ISLAMIC_ASPECT"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Command).IsNotNull();
        await Assert.That(result.Command!.EventId).IsEqualTo(eventId);
        await Assert.That(result.DestructiveContext!.AspectKind).IsEqualTo("islamic");
        await Assert.That(result.DestructiveContext.ConfirmationPhrase).IsEqualTo("DELETE_ISLAMIC_ASPECT");
    }

    [Test]
    public async Task MapTechDelete_WhenPayloadIsConfirmed_ReturnsDeleteCommand()
    {
        var eventId = Guid.CreateVersion7();

        var result = new DeleteEventTechAspectAiActionMapper().Map(
            CreateDeletePayload(eventId, "tech", "DELETE_TECH_ASPECT"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Command).IsNotNull();
        await Assert.That(result.Command!.EventId).IsEqualTo(eventId);
        await Assert.That(result.DestructiveContext!.AspectKind).IsEqualTo("tech");
        await Assert.That(result.DestructiveContext.ConfirmationPhrase).IsEqualTo("DELETE_TECH_ASPECT");
    }

    [Test]
    public async Task MapIslamicUpsert_WhenEditContextIsMissing_ReturnsFailure()
    {
        var result = new UpsertEventIslamicAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "islamic",
                "managementContextHasEdit": false,
                "genderMode": 0
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_edit_affordance_context");
    }

    [Test]
    public async Task MapIslamicUpsert_WhenPrayerOffsetHasNoReferencePrayer_ReturnsFailure()
    {
        var result = new UpsertEventIslamicAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "islamic",
                "managementContextHasEdit": true,
                "prayerTimeOffset": 15,
                "genderMode": 0
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_reference_prayer");
    }

    [Test]
    public async Task MapTechUpsert_WhenGithubUrlIsInvalid_ReturnsFailure()
    {
        var result = new UpsertEventTechAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "tech",
                "managementContextHasEdit": true,
                "githubRepoUrl": "ftp://example.test/repo",
                "skillLevel": 0
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_github_repo_url");
    }

    [Test]
    public async Task MapTechUpsert_WhenPrizePoolHasNoCurrency_ReturnsFailure()
    {
        var result = new UpsertEventTechAspectAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "tech",
                "managementContextHasEdit": true,
                "skillLevel": 0,
                "prizePool": 100
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_prize_currency_code");
    }

    [Test]
    public async Task MapTechDelete_WhenConfirmationPhraseIsWrong_ReturnsFailure()
    {
        var result = new DeleteEventTechAspectAiActionMapper().Map(
            CreateDeletePayload(Guid.CreateVersion7(), "tech", "DELETE_ASPECT"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_destructive_confirmation");
    }

    [Test]
    public async Task MapTechUpsert_WhenParsedActionKindDiffers_ReturnsUnsupportedActionKindFailure()
    {
        var action = new AiParsedProposedAction(
            AiProposedActionKind.UpsertEventIslamicAspect,
            "{}",
            "Wrong kind");

        var result = new UpsertEventTechAspectAiActionMapper().Map(action);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_action_kind");
    }

    private static string CreateDeletePayload(Guid eventId, string aspectKind, string confirmationPhrase)
        => $$"""
             {
               "eventId": "{{eventId}}",
               "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
               "aspectKind": "{{aspectKind}}",
               "managementContextHasEdit": true,
               "destructiveSummary": "Remove stale aspect metadata",
               "confirmationPhrase": "{{confirmationPhrase}}",
               "acknowledgedConsequences": true
             }
             """;
}
