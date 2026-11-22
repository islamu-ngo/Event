using System.Text.Json;
using Explore.Application.Features.Announcements;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Settings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Event.Application.UnitTests.Announcements;

public sealed class AnnouncementSettingValueCodecTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid Edition = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid Epoch = Guid.Parse("00000000-0000-4000-8000-000000000003");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompleteGroupRoundTripPreservesMaterialAndBothIdentities(bool enabled)
    {
        var group = new AnnouncementGroup(Epoch, enabled,
            new(Edition, new("A notice", "Details", "/details")));

        string stored = AnnouncementSettingValueCodec.SerializeGroup(group);

        await Assert.That(AnnouncementSettingValueCodec.DeserializeGroup(stored)).IsEqualTo(group);
        using var json = JsonDocument.Parse(stored);
        await Assert.That(json.RootElement.GetProperty("selectionEpoch").GetGuid()).IsEqualTo(Epoch);
        await Assert.That(json.RootElement.GetProperty("publication").GetProperty("edition").GetGuid()).IsEqualTo(Edition);
    }

    [Test]
    public async Task AbsentRowAndRetainedRemovalTombstoneRemainDifferent()
    {
        var tombstone = new AnnouncementGroup(Epoch, false, null);

        await Assert.That(AnnouncementSettingValueCodec.DeserializeGroup(null)).IsNull();
        await Assert.That(AnnouncementSettingValueCodec.DeserializeGroup(
            AnnouncementSettingValueCodec.SerializeGroup(tombstone))).IsEqualTo(tombstone);
    }

    [Test]
    [Arguments("")]
    [Arguments("null")]
    [Arguments("{}")]
    [Arguments("7")]
    [Arguments("{\"selectionEpoch\":\"00000000-0000-4000-8000-000000000003\",\"enabled\":true,\"publication\":null}")]
    [Arguments("{\"selectionEpoch\":\"00000000-0000-4000-8000-000000000003\",\"enabled\":false,\"publication\":null,\"revision\":7}")]
    [Arguments("{\"selectionEpoch\":\"00000000-0000-4000-8000-000000000003\",\"enabled\":false,\"enabled\":true,\"publication\":null}")]
    [Arguments("{\"selectionEpoch\":\"00000000-0000-0000-0000-000000000000\",\"enabled\":false,\"publication\":null}")]
    public async Task InvalidStoredGroupsCannotBecomeAbsenceOrPartialMaterial(string stored)
    {
        await Assert.That(() => AnnouncementSettingValueCodec.DeserializeGroup(stored)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MarkerRoundTripRetainsTenantOwnerEditionAndExactWitness(bool tenantOwned)
    {
        var marker = new AnnouncementDismissalMarker(new(Tenant,
            tenantOwned ? AnnouncementOwner.ForTenant(Tenant) : AnnouncementOwner.Instance,
            Edition, new(Epoch, false, tenantOwned ? Epoch : null, false)));

        await Assert.That(AnnouncementSettingValueCodec.DeserializeMarker(
            AnnouncementSettingValueCodec.SerializeMarker(marker))).IsEqualTo(marker);
        await Assert.That(AnnouncementSettingValueCodec.DeserializeMarker(null)).IsNull();
    }

    [Test]
    [Arguments("7")]
    [Arguments("null")]
    [Arguments("{}")]
    [Arguments("{\"tenantId\":\"00000000-0000-4000-8000-000000000001\",\"owner\":\"other\",\"edition\":\"00000000-0000-4000-8000-000000000002\",\"sourceWitness\":{}}")]
    public async Task InvalidMarkersDoNotAcquireAbsentMarkerSemantics(string stored)
    {
        await Assert.That(() => AnnouncementSettingValueCodec.DeserializeMarker(stored)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConcreteOnboardingInputMapsToTheServerSelectedOwner()
    {
        const string json = """
            {"announcement":{"change":{"kind":"publish","enabled":true,"material":{"message":"Notice","linkText":"","linkUrl":""},
            "redisplay":false},"expectedSource":{"owner":{"tenantId":"00000000-0000-4000-8000-000000000001"},
            "group":null,"isLocked":false}}}
            """;
        var request = JsonSerializer.Deserialize<UpdateTenantPolicyRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The fixture requires a request.");
        var update = AnnouncementTransportMapper.ToNative(request.Announcement
            ?? throw new InvalidOperationException("The fixture requires an explicit announcement intent."),
            AnnouncementOwner.ForTenant(Tenant));

        await Assert.That(update.Expected).IsEqualTo(
            new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(Tenant), null, false));
        await Assert.That(update.Change is AnnouncementChange.Publish { Enabled: true, Redisplay: false }).IsTrue();
    }

    [Test]
    [Arguments("{\"announcementBarMessage\":\"old input\"}")]
    [Arguments("{\"announcement\":{\"operation\":\"publish\",\"owner\":\"instance\",\"expected\":{}}}")]
    public async Task RetiredOrInjectedOnboardingFieldsAreRejectedRatherThanIgnored(string json)
    {
        await Assert.That(() => JsonSerializer.Deserialize<UpdateTenantPolicyRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))).Throws<JsonException>();
    }

    [Test]
    public async Task GenericHierarchyDoesNotTreatARemovalTombstoneAsTenantMaterial()
    {
        string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        string instance = AnnouncementSettingValueCodec.SerializeGroup(
            new(Epoch, true, new(Edition, new("Instance material", "", ""))));
        var resolved = HierarchicalSettingMerge.Resolve(key,
            new Dictionary<string, SystemSetting>(StringComparer.Ordinal)
            {
                [key] = new() { SettingKey = key, Value = instance, ValueType = SettingValueType.Json }
            },
            new Dictionary<string, TenantSetting>(StringComparer.Ordinal)
            {
                [key] = new()
                {
                    TenantId = Tenant, Tenant = null!, SettingKey = key,
                    Value = AnnouncementSettingValueCodec.SerializeGroup(new(Epoch, false, null))
                }
            }) ?? throw new InvalidOperationException("The hierarchy must resolve the existing instance publication.");

        await Assert.That(resolved.Value).IsEqualTo(instance);
        await Assert.That(resolved.Source).IsEqualTo(SettingSource.SystemDefault);
    }
}
