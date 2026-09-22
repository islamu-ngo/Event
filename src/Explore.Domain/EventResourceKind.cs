namespace Explore.Domain;

public sealed class EventResourceKind
{
    public int Id { get; set; }
    public string MasterCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public static IReadOnlyList<EventResourceKind> CreateDefaults() => Array.AsReadOnly<EventResourceKind>(
    [
        new() { Id = 1, MasterCode = "GENERAL_DOCUMENT", FullName = "General document" },
        new() { Id = 2, MasterCode = "PRESENTATION", FullName = "Presentation" },
        new() { Id = 3, MasterCode = "SPEAKER_MATERIAL", FullName = "Speaker material" },
        new() { Id = 4, MasterCode = "ATTENDEE_HANDBOOK", FullName = "Attendee handbook" },
        new() { Id = 5, MasterCode = "SCHEDULE", FullName = "Schedule" },
        new() { Id = 6, MasterCode = "WORKSHEET", FullName = "Worksheet" },
        new() { Id = 7, MasterCode = "RECORDING", FullName = "Recording" },
        new() { Id = 8, MasterCode = "LIVESTREAM", FullName = "Livestream" },
        new() { Id = 9, MasterCode = "VIRTUAL_MEETING", FullName = "Virtual meeting" },
        new() { Id = 10, MasterCode = "CERTIFICATE", FullName = "Shared certificate" },
        new() { Id = 11, MasterCode = "SPONSOR_MATERIAL", FullName = "Sponsor material" },
        new() { Id = 12, MasterCode = "ORGANIZER_INTERNAL", FullName = "Organizer internal" },
        new() { Id = 13, MasterCode = "OTHER", FullName = "Other" }
    ]);
}
