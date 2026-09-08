// Contains the lists of included/excluded category IDs and their combination modes.

namespace Explore.Blazor.Client.Models;

public class CategoryFilterChangedEventArgs
{
    public List<Guid> IncludedCategoryIds { get; set; } = [];
    public List<Guid> ExcludedCategoryIds { get; set; } = [];
    public string InclusionMode { get; set; } = "and";
    public string ExclusionMode { get; set; } = "or";
}
