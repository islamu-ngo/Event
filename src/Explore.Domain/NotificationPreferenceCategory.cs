namespace Explore.Domain;

public class NotificationPreferenceCategory
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool DefaultEmailEnabled { get; set; }
    public bool DefaultInAppEnabled { get; set; }
    public bool DefaultPushEnabled { get; set; }
    public int SortOrder { get; set; }
}
