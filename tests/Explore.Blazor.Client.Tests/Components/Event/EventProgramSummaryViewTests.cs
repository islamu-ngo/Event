using Explore.Blazor.Client.Clients;
using EventProgramSummaryViewComponent = Explore.Blazor.Client.Pages.Events.Components.EventProgramSummaryView;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventProgramSummaryViewTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenSummaryHasGroups_RendersSectionsDaysItemsAndWarnings()
    {
        var cut = _ctx.RenderMudComponent<EventProgramSummaryViewComponent>(parameters => parameters
            .Add(component => component.Summary, CreateSummary()));

        await Assert.That(cut.Markup).Contains("Program schedule");
        await Assert.That(cut.Markup).Contains("Europe/Brussels");
        await Assert.That(cut.Markup).Contains("Main stage");
        await Assert.That(cut.Markup).Contains("Keynotes");
        await Assert.That(cut.Markup).Contains("Fri 3 Jul");
        await Assert.That(cut.Markup).Contains("Opening keynote");
        await Assert.That(cut.Markup).Contains("09:00–10:15");
        await Assert.That(cut.Markup).Contains("Auditorium");
        await Assert.That(cut.Markup).Contains("250 seats");
        await Assert.That(cut.Markup).Contains("Open registration");
        await Assert.That(cut.Markup).Contains("Program readiness guidance");
        await Assert.That(cut.Markup).Contains("Assign at least one speaker before publishing.");
    }

    [Test]
    public async Task Render_WhenSummaryHasNoSections_ShowsEmptyState()
    {
        var cut = _ctx.RenderMudComponent<EventProgramSummaryViewComponent>(parameters => parameters
            .Add(component => component.Summary, new EventProgramSummaryDto
            {
                EventTitle = "Empty program",
                Sections = []
            }));

        await Assert.That(cut.Markup).Contains("No program items are available yet");
    }

    public void Dispose() => _ctx.Dispose();

    private static EventProgramSummaryDto CreateSummary()
    {
        return new EventProgramSummaryDto
        {
            EventTitle = "Program launch",
            TimeZoneId = "Europe/Brussels",
            ReadinessWarnings = new List<EventProgramReadinessWarningDto>
            {
                new()
                {
                    Path = "program.sessions[0].speakers",
                    Severity = "warning",
                    Message = "Assign at least one speaker before publishing."
                }
            },
            Sections = new List<EventProgramSectionDto>
            {
                new()
                {
                    SectionKey = "main-stage",
                    Title = "Main stage",
                    SortOrder = 1,
                    SessionGroups = new List<EventProgramSessionGroupSectionDto>
                    {
                        new()
                        {
                            Title = "Keynotes",
                            RoomName = "Auditorium",
                            SortOrder = 1,
                            Days = new List<EventProgramDayGroupDto>
                            {
                                new()
                                {
                                    LocalDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
                                    DisplayLabel = "Fri 3 Jul",
                                    Items = new List<EventProgramItemDto>
                                    {
                                        new()
                                        {
                                            Title = "Opening keynote",
                                            LocalDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
                                            LocalStartTime = new TimeSpan(9, 0, 0),
                                            LocalEndTime = new TimeSpan(10, 15, 0),
                                            RoomName = "Auditorium",
                                            EventSessionKindId = 1,
                                            EventSessionKindName = "Talk",
                                            EventSessionKindMasterCode = "TALK",
                                            Capacity = 250,
                                            RegistrationModeName = "Open registration"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
