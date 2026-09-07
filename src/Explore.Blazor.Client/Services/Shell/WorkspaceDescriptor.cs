namespace Explore.Blazor.Client.Services.Shell;

using Explore.Blazor.Client.Clients;

public sealed record WorkspaceDescriptor(
    WorkspaceKey Key,
    string LabelKey,
    string Icon,
    string BaseRoute,
    bool RequiresAuthentication,
    Func<WorkspaceAvailabilityDto?, bool>? AvailabilityPolicy = null,
    Type? NavigationProviderType = null);
