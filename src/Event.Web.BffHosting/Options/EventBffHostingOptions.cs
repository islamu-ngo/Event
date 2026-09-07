using Event.Web.BffHosting.Authentication;

namespace Event.Web.BffHosting.Options;

public sealed class EventBffHostingOptions
{
    public const string SectionName = "Bff";

    public EventBffHostProfile HostProfile { get; set; } = EventBffHostProfile.PublicWeb;

    public string ApiBaseAddress { get; set; } = "https://localhost:7039/";

    public string[] AdminHosts { get; set; } = [];

    public string[] AdminHostAllowedIpRanges { get; set; } = [];

    public bool StripPrivilegedHeaders { get; set; } = true;

    public bool ForwardAccessToken { get; set; } = true;

    public bool ForwardTrustedTenantHint { get; set; } = true;

    public bool ForwardSetupSecret { get; set; } = true;

    public bool ForwardSupportAccessSession { get; set; } = true;
}
