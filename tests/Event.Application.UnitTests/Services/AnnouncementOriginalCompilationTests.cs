using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Application.Features.Announcements;

namespace Event.Application.UnitTests.Services;

public sealed class AnnouncementOriginalCompilationTests
{
    [Test]
    public async Task MaterialMessageChangeWithoutForcedRedisplayCompilesReplacementPublicationMarker()
    {
        Guid tenant = Guid.Parse("00000000-0000-4000-8000-000000000001");
        Guid edition = Guid.Parse("00000000-0000-4000-8000-000000000002");
        Guid epoch = Guid.Parse("00000000-0000-4000-8000-000000000003");
        Guid replacementEdition = Guid.Parse("00000000-0000-4000-8000-000000000004");
        Guid replacementEpoch = Guid.Parse("00000000-0000-4000-8000-000000000005");
        var original = new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(tenant),
            new(epoch, true, new(edition, new("Previous announcement", "", ""))), false);
        var settings = new UpdateTenantPolicyRequest
        {
            Announcement = AnnouncementTransportMapper.ToDto(new AnnouncementUpdate(
                new AnnouncementChange.Publish(new("New announcement", "", ""), true, false), original))
        };
        var system = new Dictionary<string, SystemSetting>(StringComparer.Ordinal);

        var effects = TenantPolicySettingService.CompileOriginal(
            settings, system, "announcement-tenant", original, replacementEdition, replacementEpoch);

        await Assert.That(effects.Any(effect =>
            effect.Key == GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication
            && !string.IsNullOrWhiteSpace(effect.Value)
            && effect.Value != AnnouncementSettingValueCodec.SerializeGroup(original.Group
                ?? throw new InvalidOperationException("The fixture requires published material.")))).IsTrue();
        var stored = AnnouncementSettingValueCodec.DeserializeGroup(effects.Single(effect =>
            effect.Key == GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication).Value)
            ?? throw new InvalidOperationException("The compiler must emit a complete publication.");
        await Assert.That(stored.Publication?.Edition).IsEqualTo(replacementEdition);
        await Assert.That(stored.SelectionEpoch).IsEqualTo(replacementEpoch);
        await Assert.That(stored.Publication?.Material.Message).IsEqualTo("New announcement");
    }

    [Test]
    public async Task UnrelatedOnboardingInputDoesNotPublishInheritedAnnouncementMaterial()
    {
        var effects = TenantPolicySettingService.CompileOriginal(new UpdateTenantPolicyRequest(),
            new Dictionary<string, SystemSetting>(StringComparer.Ordinal), "announcement-tenant", null, null, null);

        await Assert.That(effects.Any(effect => AnnouncementSettingMutationGuard.Handles(effect.Key))).IsFalse();
    }
}
