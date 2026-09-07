namespace Explore.Domain.Settings.Documents.Payloads;

public sealed record RenderPolicySettings
{
    public bool ShowOrganizationBranding { get; init; } = true;

    public bool ShowGroupBranding { get; init; } = true;
}
