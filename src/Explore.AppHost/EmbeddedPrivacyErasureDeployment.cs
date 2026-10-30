using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Explore.AppHost;

internal static class EmbeddedPrivacyErasureDeployment
{
    internal static Action<IResourceBuilder<ContainerResource>> Configure(string? volumeName) => container => container
        .WithEnvironment("PrivacyErasureAuthorityEmbedded__Path", "/app/data/privacy_erasure_authority.db")
        .WithVolume(volumeName ?? "islamu-event-privacy-erasure-authority-data", "/app/data");
}
