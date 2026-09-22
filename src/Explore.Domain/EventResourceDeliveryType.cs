namespace Explore.Domain;

public sealed class EventResourceDeliveryType
{
    public int Id { get; set; }
    public string MasterCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public static IReadOnlyList<EventResourceDeliveryType> CreateDefaults() => Array.AsReadOnly<EventResourceDeliveryType>(
    [
        new() { Id = 1, MasterCode = "STORED_FILE", FullName = "Stored file" },
        new() { Id = 2, MasterCode = "EXTERNAL_LINK", FullName = "External link" }
    ]);
}
