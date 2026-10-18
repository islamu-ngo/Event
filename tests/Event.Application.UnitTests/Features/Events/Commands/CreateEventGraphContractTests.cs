using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.AiAssistant.Actions;

namespace Event.Application.UnitTests.Features.Events.Commands;

public sealed class CreateEventGraphContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task OmittedSchedule_RetainsTitleOnlyDraftWithoutInventingAnInstant()
    {
        var session = JsonSerializer.Deserialize<CreateEventGraphSessionDto>(
            """{"tempKey":"opening","title":"Opening discussion","languageIds":[1,2],"sortOrder":3}""",
            JsonOptions)!;

        await Assert.That((object?)session.StartTime).IsNull();
        await Assert.That(session.EndTime).IsNull();
        await Assert.That(session.Title).IsEqualTo("Opening discussion");
        await Assert.That(session.TempKey).IsEqualTo("opening");
        await Assert.That(session.LanguageIds).IsEquivalentTo([1, 2]);
        await Assert.That(session.SortOrder).IsEqualTo(3);

        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(session, JsonOptions));
        await Assert.That(serialized.RootElement.GetProperty("startTime").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(serialized.RootElement.GetProperty("endTime").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task ScheduledDraft_RetainsSubmittedOffsetAndOvernightInterval()
    {
        var session = JsonSerializer.Deserialize<CreateEventGraphSessionDto>(
            """{"title":"Evening program","startTime":"2026-10-12T23:30:00+02:00","endTime":"2026-10-13T01:00:00+02:00"}""",
            JsonOptions)!;

        await Assert.That((DateTimeOffset?)session.StartTime)
            .IsEqualTo(new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.FromHours(2)));
        await Assert.That(session.EndTime)
            .IsEqualTo(new DateTimeOffset(2026, 10, 13, 1, 0, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task AiTitleOnlySession_RetainsPreparedDraftInsteadOfDiscardingIt()
    {
        var mapped = new CreateEventDraftAiActionMapper().Map(
            """
            {
              "title": "Community gathering",
              "session": { "title": "Opening discussion", "languageIds": [1] },
              "participationConfiguration": {
                "participationHandlingModeId": 1,
                "advanceRegistrationObligationId": 1
              }
            }
            """);

        await Assert.That(mapped.Succeeded).IsTrue();
        await Assert.That(mapped.Draft!.Sessions.Count).IsEqualTo(1);
        var session = mapped.Draft.Sessions.Single();
        await Assert.That(session.Title).IsEqualTo("Opening discussion");
        await Assert.That((object?)session.StartTime).IsNull();
        await Assert.That(session.EndTime).IsNull();
        await Assert.That(session.LanguageIds).IsEquivalentTo([1]);
    }
}
