using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.Location;
using Explore.Domain;

namespace Explore.Application.Features.EventPrograms.Models;

internal sealed class ProgramGroupAccumulator
{
    private ProgramGroupAccumulator(
        string sectionKey,
        Guid? sessionGroupId,
        string title,
        int sortOrder,
        string? color,
        EventLocationPublicDto? eventLocation)
    {
        SectionKey = sectionKey;
        SessionGroupId = sessionGroupId;
        Title = title;
        SortOrder = sortOrder;
        Color = color;
        EventLocation = eventLocation;
    }

    public string SectionKey { get; }
    public Guid? SessionGroupId { get; }
    public string Title { get; }
    public int SortOrder { get; }
    public string? Color { get; }
    public EventLocationPublicDto? EventLocation { get; }
    public List<EventProgramItemDto> Items { get; } = [];

    public static ProgramGroupAccumulator FromGroup(
        EventSessionGroup group,
        EventLocationPublicDto? eventLocation)
    {
        return new ProgramGroupAccumulator(
            group.Id.ToString(),
            group.Id,
            group.Name,
            group.SortOrder,
            group.Color,
            eventLocation);
    }

    public static ProgramGroupAccumulator Unassigned(string sectionKey, int sortOrder)
    {
        return new ProgramGroupAccumulator(
            sectionKey,
            null,
            "Unassigned program items",
            sortOrder,
            null,
            null);
    }
}
