using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain.Settings;

[assembly: InternalsVisibleTo("Explore.Persistence")]

namespace Explore.Application.Features.ConfigurationManifest.Importing;

public sealed record AnnouncementImportIntent(AnnouncementOwner Owner, AnnouncementChange Change);

/// <summary>
/// A native-issued reference, not a service contract. Public factories and stores cannot
/// register themselves or substitute their own verifier for the retained native callbacks.
/// </summary>
internal sealed class HumanConfigurationImportAnnouncementOwnership
{
    private static readonly ConditionalWeakTable<IHumanConfigurationImportAnnouncementStore,
        HumanConfigurationImportAnnouncementOwnership> Issued = new();

    private readonly IHumanConfigurationImportAnnouncementStore store;
    private readonly IHumanConfigurationImportAnnouncementFactory factory;
    private readonly object selectedOwner;
    private readonly IHumanConfigurationImportAdmission issuer;
    private readonly HumanConfigurationImportAuthority authority;
    private readonly Action<IHumanConfigurationImportAnnouncementFactory, object,
        IHumanConfigurationImportAnnouncementStore> requireNativeOwner;
    private readonly Func<CancellationToken, Task> requireNativeCompletion;

    private HumanConfigurationImportAnnouncementOwnership(IHumanConfigurationImportAnnouncementStore store,
        IHumanConfigurationImportAnnouncementFactory factory, object selectedOwner,
        IHumanConfigurationImportAdmission issuer, HumanConfigurationImportAuthority authority,
        Action<IHumanConfigurationImportAnnouncementFactory, object,
            IHumanConfigurationImportAnnouncementStore> requireNativeOwner,
        Func<CancellationToken, Task> requireNativeCompletion)
    {
        this.store = store;
        this.factory = factory;
        this.selectedOwner = selectedOwner;
        this.issuer = issuer;
        this.authority = authority;
        this.requireNativeOwner = requireNativeOwner;
        this.requireNativeCompletion = requireNativeCompletion;
    }

    internal static void Register(IHumanConfigurationImportAnnouncementStore store,
        IHumanConfigurationImportAnnouncementFactory factory, object selectedOwner,
        IHumanConfigurationImportAdmission issuer, HumanConfigurationImportAuthority authority,
        Action<IHumanConfigurationImportAnnouncementFactory, object,
            IHumanConfigurationImportAnnouncementStore> requireNativeOwner,
        Func<CancellationToken, Task> requireNativeCompletion)
    {
        var ownership = new HumanConfigurationImportAnnouncementOwnership(store, factory, selectedOwner,
            issuer, authority, requireNativeOwner, requireNativeCompletion);
        ownership.RequireCurrent(store);
        Issued.Add(store, ownership);
    }

    internal static HumanConfigurationImportAnnouncementOwnership Require(
        IHumanConfigurationImportAnnouncementStore store, IHumanConfigurationImportAdmission issuer,
        HumanConfigurationImportAuthority authority)
    {
        if (!Issued.TryGetValue(store, out var ownership)
            || !ReferenceEquals(ownership.issuer, issuer) || !ReferenceEquals(ownership.authority, authority))
            throw new UnauthorizedAccessException("Announcement import requires its independently issued native ownership.");
        ownership.RequireCurrent(store);
        return ownership;
    }

    internal void RequireCurrent(IHumanConfigurationImportAnnouncementStore candidate)
    {
        if (!ReferenceEquals(store, candidate))
            throw new UnauthorizedAccessException("Announcement import cannot substitute another store.");
        requireNativeOwner(factory, selectedOwner, store);
    }

    internal async Task RequireCompletedAsync(CancellationToken cancellationToken)
    {
        RequireCurrent(store);
        await requireNativeCompletion(cancellationToken);
        RequireCurrent(store);
    }
}

/// <summary>The actual section compiler's immutable slice; possession does not grant import authority.</summary>
public sealed class HumanConfigurationImportAnnouncement
{
    internal HumanConfigurationImportAnnouncement(Guid operationId, HumanCommitSubject subject,
        ConfigurationImportTarget target, string artifactDigest, AnnouncementImportIntent intent,
        IHumanConfigurationImportAnnouncementStore store)
    {
        OperationId = operationId;
        Subject = subject;
        Target = target;
        ArtifactDigest = artifactDigest;
        Intent = intent;
        Store = store;
    }

    public Guid OperationId { get; }
    public HumanCommitSubject Subject { get; }
    public ConfigurationImportTarget Target { get; }
    public string ArtifactDigest { get; }
    public AnnouncementImportIntent Intent { get; }
    public IHumanConfigurationImportAnnouncementStore Store { get; }
    public override string ToString() => nameof(HumanConfigurationImportAnnouncement);
}

public interface IHumanConfigurationImportAnnouncementFactory
{
    void RequireAnnouncementStore(IHumanConfigurationImportAnnouncementStore store);
    Task<IHumanConfigurationImportAnnouncementStore> ObserveAnnouncementAsync(
        HumanConfigurationImportAuthority authority, Guid operationId, string artifactDigest,
        ImmutableArray<AnnouncementImportIntent> intents, DateTime occurredAt, CancellationToken cancellationToken);
}

public interface IHumanConfigurationImportAnnouncementStore
{
    Task RequireCurrentAsync(CancellationToken cancellationToken);
    Task<ImmutableArray<HumanConfigurationImportSettingEffect>> CompileAsync(
        HumanConfigurationImportApply original, ImmutableArray<HumanConfigurationImportAnnouncement> announcements,
        CancellationToken cancellationToken);
    Task<ImmutableArray<SettingChangedNotification>> ApplyAsync(
        HumanConfigurationImportAnnouncement announcement, CancellationToken cancellationToken);
    Task RequireCompletedAsync(CancellationToken cancellationToken);
}
