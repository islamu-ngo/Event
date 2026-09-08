using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Components.Registration.ProviderLaunch;

public sealed record RegistrationProviderLaunchContext(
    Guid EventId,
    Guid OrderId,
    RegistrationProviderLaunchLineage Lineage,
    GuestRegistrationOrderCapability? GuestCapability = null);

public sealed record RegistrationProviderBffLaunch(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    string? GuestCapability);

public sealed record RegistrationProviderBffTicket(string EmbedUrl);

public sealed record RegistrationProviderPollingSnapshot(
    NativeRegistrationRequirementCollectionView? Requirements,
    object? OrderResource);

public sealed class RegistrationProviderLaunchState(
    IBffClient bffClient,
    INativeRegistrationFormService nativeRegistrationFormService,
    IRegistrationOrderService registrationOrderService,
    IBrowserActionInterop browserActions)
{
    public RegistrationProviderLaunchContext? Context { get; private set; }
    public string? EmbedUrl { get; private set; }
    public RegistrationProviderPollingSnapshot? Snapshot { get; private set; }
    public bool IframeLoaded { get; private set; }
    public bool IsComplete => Context is { } context && Snapshot?.Requirements?.Requirements
        .SingleOrDefault(requirement => requirement.RequirementId == context.Lineage.RequirementId)
        ?.Progress.IsComplete == true;

    public static bool CanLaunch(IReadOnlyDictionary<string, HalLink>? links) =>
        links?.ContainsKey("launch-descriptor") == true;

    public async Task InitializeAsync(RegistrationProviderLaunchContext context, CancellationToken cancellationToken = default)
    {
        Context = context;
        RegistrationProviderBffTicket? launch = await bffClient.SendAsync<RegistrationProviderBffLaunch, RegistrationProviderBffTicket>(
            HttpMethod.Post,
            "/bff/registration-provider-embed/launches",
            new(context.EventId, context.OrderId, context.Lineage.RequirementId, context.Lineage.ChannelId,
                context.Lineage.BindingId, context.Lineage.FormId, context.Lineage.FormVersionId,
                context.GuestCapability?.Value),
            cancellationToken);
        EmbedUrl = launch?.EmbedUrl;
        await PollAsync(cancellationToken);
    }

    public Task<bool> OpenAuthorizedNewTabAsync(CancellationToken cancellationToken = default) =>
        EmbedUrl is { Length: > 0 }
            ? browserActions.OpenSameOriginNewTabAsync(EmbedUrl, cancellationToken)
            : Task.FromResult(false);

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (Context is not { } context)
        {
            return;
        }

        var requirements = await nativeRegistrationFormService.GetRequirementsAsync(
            context.EventId,
            context.OrderId,
            context.GuestCapability,
            cancellationToken);
        object? order = context.GuestCapability is { } guest
            ? await registrationOrderService.GetGuestAsync(context.EventId, context.OrderId, guest, cancellationToken)
            : await registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, cancellationToken);
        Snapshot = new RegistrationProviderPollingSnapshot(requirements, order);
    }

    public Task OnIframeLoadedAsync()
    {
        IframeLoaded = true;
        return Task.CompletedTask;
    }

    public Task OnIframeNavigatedAsync() => Task.CompletedTask;
}
