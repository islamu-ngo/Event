using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Models.CustomProperties;

/// <summary>
/// Snapshot of the governance flags that drive projection exposure / discovery / export behavior.
/// </summary>
/// <remarks>
/// The backend endpoint is a full PUT of the definition. The admin service merges this snapshot
/// into the fetched <see cref="CustomPropertyDefinitionDto"/> before sending, so only the
/// governance-relevant fields change.
/// </remarks>
public sealed class DefinitionFlagUpdateModel
{
    public Guid DefinitionId { get; set; }
    public ExposureLevel ExposureLevel { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsExportable { get; set; }
    public bool IsModerationRelevant { get; set; }
    public bool IsAnalyticsRelevant { get; set; }
}
