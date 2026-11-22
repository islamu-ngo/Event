using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Persistence.Services;

public sealed partial class TypedSettingsOriginalStore
{
    private AnnouncementPublicationResult? announcementResult;

    private AnnouncementSourceSnapshot ReadAnnouncementSource(Guid? tenantId)
    {
        const string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        if (tenantId is { } tenant)
        {
            var row = Rows<TenantSetting>().SingleOrDefault(value => value.TenantId == tenant && value.SettingKey == key);
            return new(AnnouncementOwner.ForTenant(tenant),
                AnnouncementSettingValueCodec.DeserializeGroup(row?.Value), row?.IsLocked ?? false);
        }
        var system = Rows<SystemSetting>().SingleOrDefault(value => value.SettingKey == key);
        return new(AnnouncementOwner.Instance,
            AnnouncementSettingValueCodec.DeserializeGroup(system?.Value), system?.IsLocked ?? false);
    }

    private ImmutableArray<TypedSettingsEffect> CompileAnnouncement(TypedSettingsOriginal request)
    {
        var announcement = request.Announcement
            ?? throw new UnauthorizedAccessException("The exact announcement original is required.");
        var current = ReadAnnouncementSource(request.TenantId);
        if (announcement.Update.Expected != current)
        {
            announcementResult = new(AnnouncementPublicationOutcome.SourceChanged, current);
            return [];
        }
        bool changed = AnnouncementPublicationPolicy.RequiresSelectionEpoch(
            current.Group, current.IsLocked, announcement.Update.Change);
        if (!changed)
        {
            announcementResult = new(AnnouncementPublicationOutcome.Unchanged, current);
            return [];
        }
        if (announcement.Producer == "announcement-tenant"
            && (current.IsLocked || ReadAnnouncementSource(null).IsLocked))
        {
            announcementResult = new(AnnouncementPublicationOutcome.Locked, current);
            return [];
        }
        Guid? edition = AnnouncementPublicationPolicy.RequiresMaterialEdition(current.Group, announcement.Update.Change)
            ? Guid.NewGuid() : null;
        var write = AnnouncementPublicationPolicy.Apply(current.Owner, current.Group, current.IsLocked,
            announcement.Update.Change, edition, Guid.NewGuid());
        announcementResult = new(AnnouncementPublicationOutcome.Applied, new(current.Owner, write.Group, write.IsLocked));
        return [new(GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication,
            AnnouncementSettingValueCodec.SerializeGroup(write.Group
                ?? throw new InvalidOperationException("An announcement transition must retain its group.")),
            request.TenantId is null || announcement.Update.Change is AnnouncementChange.SetLock ? write.IsLocked : null)];
    }

    private ImmutableArray<TypedSettingsEffect> CompileOnboardingOriginal(
        TypedSettingsOriginal request, IReadOnlyDictionary<string, SystemSetting> system, string? slug)
    {
        var onboarding = request.Onboarding
            ?? throw new UnauthorizedAccessException("The original onboarding input is required.");
        AnnouncementSourceSnapshot? current = null;
        Guid? edition = null, epoch = null;
        if (request.OnboardingAnnouncement is { } update)
        {
            current = ReadAnnouncementSource(request.TenantId);
            if (current != update.Expected)
                throw new ArgumentException("The announcement source changed before onboarding publication.");
            if (update.Change is AnnouncementChange.SetLock)
                throw new ArgumentException("Announcement locks require their dedicated original command.");
            bool changed = AnnouncementPublicationPolicy.RequiresSelectionEpoch(current.Group, current.IsLocked, update.Change);
            if (changed && (current.IsLocked || ReadAnnouncementSource(null).IsLocked))
                throw new UnauthorizedAccessException("The acquired announcement source is locked.");
            if (AnnouncementPublicationPolicy.RequiresMaterialEdition(current.Group, update.Change))
                edition = Guid.NewGuid();
            if (changed)
                epoch = Guid.NewGuid();
        }
        return TenantPolicySettingService.CompileOriginal(onboarding.Settings, system, slug, current, edition, epoch);
    }

    public async Task<(AnnouncementPublicationResult Result, ImmutableArray<SettingChangedNotification> DeferredNotifications)>
        ApplyAnnouncementAsync(TypedSettingsOriginal request, CancellationToken cancellationToken)
    {
        try
        {
            Require(request);
            if (request.Announcement is null || announcementResult is null || invoked)
                throw new UnauthorizedAccessException("Announcement publication requires its unconsumed original compilation.");
            invoked = true;
            var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
            foreach (var effect in effects)
            {
                var change = await WriteAsync(effect, cancellationToken);
                if (change is not null)
                    notifications.Add(new(effect.Key, change.OldValue, change.NewValue,
                        request.TenantId.HasValue ? SettingSource.TenantOverride
                            : effect.IsLocked == true ? SettingSource.SystemLocked : SettingSource.SystemDefault,
                        request.TenantId, request.Actor, occurred));
            }
            await VerifyAsync(request, cancellationToken);
            return (announcementResult, notifications.ToImmutable());
        }
        catch { closed = true; throw; }
    }
}
