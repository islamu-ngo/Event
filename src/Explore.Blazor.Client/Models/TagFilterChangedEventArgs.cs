// Contains the lists of included/excluded tag IDs and their combination modes.

namespace Explore.Blazor.Client.Models;

public class TagFilterChangedEventArgs
{
    public List<Guid> IncludedTagIds { get; set; } = [];
    public List<Guid> ExcludedTagIds { get; set; } = [];
    public string InclusionMode { get; set; } = "and";
    public string ExclusionMode { get; set; } = "or";
}
