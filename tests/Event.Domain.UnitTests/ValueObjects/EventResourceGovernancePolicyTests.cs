using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class EventResourceGovernancePolicyTests
{
    private const string Pdf = "application/pdf";
    private const string Word = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PowerPoint = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    [Test]
    public async Task Definitions_PublishExactDefaultsAndScopes()
    {
        var definitions = EventResourceSettingDefinitions.All;

        await Assert.That(definitions.Select(x => x.Key)).IsEquivalentTo(new[]
        {
            GovernanceSettingKeys.EventResources.EnabledDeliveryTypes,
            GovernanceSettingKeys.EventResources.EnabledAudiences,
            GovernanceSettingKeys.EventResources.PermittedFileTypes,
            GovernanceSettingKeys.EventResources.MaxUploadBytes,
            GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
            GovernanceSettingKeys.EventResources.ExternalOrigins,
            GovernanceSettingKeys.EventResources.AuditRetentionDays,
            GovernanceSettingKeys.EventResources.MaxActiveResources
        });
        await Assert.That(definitions.All(x => x.RequiresCoordinatedMutation)).IsTrue();
        await Assert.That(definitions.All(x => SettingRegistry.Get(x.Key) == x)).IsTrue();
        await Assert.That(JsonSerializer.Deserialize<string[]>(EventResourceSettingDefinitions.EnabledDeliveryTypes.DefaultValue))
            .IsEquivalentTo(new[] { nameof(EventResourceDeliveryTypeEnum.StoredFile), nameof(EventResourceDeliveryTypeEnum.ExternalLink) });
        await Assert.That(JsonSerializer.Deserialize<string[]>(EventResourceSettingDefinitions.EnabledAudiences.DefaultValue))
            .IsEquivalentTo(Enum.GetNames<EventResourceAudienceKindEnum>());
        await Assert.That(JsonSerializer.Deserialize<string[]>(EventResourceSettingDefinitions.PermittedFileTypes.DefaultValue))
            .IsEquivalentTo(new[] { Pdf, Word, PowerPoint });
        await Assert.That(EventResourceSettingDefinitions.MaxUploadBytes.DefaultValue).IsEqualTo("10485760");
        await Assert.That(EventResourceSettingDefinitions.AllowUnscannedDocuments.DefaultValue).IsEqualTo("false");
        await Assert.That(EventResourceSettingDefinitions.ExternalOrigins.DefaultValue).IsEqualTo("[]");
        await Assert.That(EventResourceSettingDefinitions.AuditRetentionDays.DefaultValue).IsEqualTo("30");
        await Assert.That(EventResourceSettingDefinitions.MaxActiveResources.DefaultValue).IsEqualTo("500");
        await Assert.That(EventResourceSettingDefinitions.AllowUnscannedDocuments.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(EventResourceSettingDefinitions.AllowUnscannedDocuments.MaxScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definitions.Where(x => x != EventResourceSettingDefinitions.AllowUnscannedDocuments)
            .All(x => x.MaxScope == SettingScope.Tenant)).IsTrue();
    }

    [Test]
    public async Task Create_RejectsUnknownAndMixedClosedSets()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(
            deliveryTypes: [EventResourceDeliveryTypeEnum.StoredFile, (EventResourceDeliveryTypeEnum)99])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(
            audiences: [EventResourceAudienceKindEnum.Public, (EventResourceAudienceKindEnum)99])));
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(Create(fileTypes: [Pdf, "text/html"])));
    }

    [Test]
    [Arguments("http://files.example.com")]
    [Arguments("https://user@files.example.com")]
    [Arguments("https://files.example.com/path")]
    [Arguments("https://files.example.com?query=1")]
    [Arguments("https://files.example.com#fragment")]
    [Arguments("https://*.example.com")]
    [Arguments("https://FILES.example.com")]
    [Arguments("https://files.example.com/")]
    public async Task Create_RejectsMalformedOrNonCanonicalOrigins(string origin) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(Create(origins: [origin])));

    [Test]
    public async Task Default_UsesPublishedDefaultsAndStorageCeiling()
    {
        var policy = EventResourceGovernancePolicy.Default(storageMaxUploadBytes: 5_000_000);

        await Assert.That(policy.EnabledDeliveryTypes).IsEquivalentTo(Enum.GetValues<EventResourceDeliveryTypeEnum>());
        await Assert.That(policy.EnabledAudiences).IsEquivalentTo(Enum.GetValues<EventResourceAudienceKindEnum>());
        await Assert.That(policy.PermittedFileTypes).IsEquivalentTo(new[] { Pdf, Word, PowerPoint });
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(5_000_000);
        await Assert.That(policy.AllowUnscannedDocuments).IsFalse();
        await Assert.That(policy.ExternalOrigins).IsEmpty();
        await Assert.That(policy.AuditRetentionDays).IsEqualTo(30);
        await Assert.That(policy.MaxActiveResources).IsEqualTo(500);
    }

    [Test]
    public async Task TenantComparison_RejectsEveryWideningAndAcceptsNarrowing()
    {
        var instance = Create(
            deliveryTypes: [EventResourceDeliveryTypeEnum.StoredFile, EventResourceDeliveryTypeEnum.ExternalLink],
            audiences: [EventResourceAudienceKindEnum.Public, EventResourceAudienceKindEnum.Organizer],
            fileTypes: [Pdf, Word], maxUploadBytes: 1000, origins: ["https://files.example.com"],
            retentionDays: 30, maxActiveResources: 100);
        var narrow = Create(deliveryTypes: [EventResourceDeliveryTypeEnum.StoredFile],
            audiences: [EventResourceAudienceKindEnum.Organizer], fileTypes: [Pdf], maxUploadBytes: 999,
            origins: [], retentionDays: 20, maxActiveResources: 99);

        await Assert.That(instance.IsNonWidening(narrow)).IsTrue();
        await Assert.That(instance.IsNonWidening(Create(fileTypes: [Pdf, PowerPoint], maxUploadBytes: 1000,
            origins: ["https://files.example.com"], retentionDays: 30, maxActiveResources: 100))).IsFalse();
        await Assert.That(instance.IsNonWidening(Create(maxUploadBytes: 1001))).IsFalse();
        await Assert.That(instance.IsNonWidening(Create(retentionDays: 31))).IsFalse();
        await Assert.That(instance.IsNonWidening(Create(maxActiveResources: 101))).IsFalse();
        await Assert.That(instance.IsNonWidening(Create(origins: ["https://other.example.com"]))).IsFalse();
    }

    [Test]
    public async Task UnscannedOptInCannotWidenInstancePolicy_AndIntersectionFailsClosed()
    {
        var tenantPolicyWithInheritedOptIn = Create(allowUnscanned: true);
        var denyingTenant = Create(allowUnscanned: false);

        await Assert.That(Create(allowUnscanned: false).IsNonWidening(tenantPolicyWithInheritedOptIn)).IsFalse();
        await Assert.That(Create(allowUnscanned: true).IsNonWidening(tenantPolicyWithInheritedOptIn)).IsTrue();
        await Assert.That(Create(allowUnscanned: true).IsNonWidening(denyingTenant)).IsTrue();
        await Assert.That(Create(allowUnscanned: false).Intersect(tenantPolicyWithInheritedOptIn).AllowUnscannedDocuments).IsFalse();
    }

    [Test]
    public async Task Intersection_RemainsSafeAfterInstanceCeilingsTighten()
    {
        var tightenedInstance = Create(deliveryTypes: [EventResourceDeliveryTypeEnum.ExternalLink],
            audiences: [EventResourceAudienceKindEnum.Organizer], fileTypes: [Pdf], maxUploadBytes: 500,
            origins: ["https://one.example.com"], retentionDays: 10, maxActiveResources: 25);
        var staleTenant = Create(fileTypes: [Pdf, Word], maxUploadBytes: 1000,
            origins: ["https://one.example.com", "https://two.example.com"], retentionDays: 30,
            maxActiveResources: 100);

        var effective = tightenedInstance.Intersect(staleTenant);

        await Assert.That(effective.EnabledDeliveryTypes).IsEquivalentTo(new[] { EventResourceDeliveryTypeEnum.ExternalLink });
        await Assert.That(effective.EnabledAudiences).IsEquivalentTo(new[] { EventResourceAudienceKindEnum.Organizer });
        await Assert.That(effective.PermittedFileTypes).IsEquivalentTo(new[] { Pdf });
        await Assert.That(effective.ExternalOrigins).IsEquivalentTo(new[] { "https://one.example.com" });
        await Assert.That(effective.MaxUploadBytes).IsEqualTo(500);
        await Assert.That(effective.AuditRetentionDays).IsEqualTo(10);
        await Assert.That(effective.MaxActiveResources).IsEqualTo(25);
    }

    [Test]
    public async Task OriginMatching_IsExplicitAndExact()
    {
        var policy = Create(origins: ["https://files.example.com", "https://files.example.com:8443"]);

        await Assert.That(policy.AllowsExternalOrigin("https://files.example.com")).IsTrue();
        await Assert.That(policy.AllowsExternalOrigin("https://files.example.com:8443")).IsTrue();
        await Assert.That(policy.AllowsExternalOrigin("https://sub.files.example.com")).IsFalse();
        await Assert.That(policy.AllowsExternalOrigin("https://files.example.com/path")).IsFalse();
    }

    [Test]
    public async Task Create_EnforcesNumericBoundsAndStorageCeiling()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(maxUploadBytes: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(retentionDays: -1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(retentionDays: 91)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(maxActiveResources: -1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Create(maxActiveResources: 501)));

        await Assert.That(Create(maxUploadBytes: 1000, storageCeiling: 600).MaxUploadBytes).IsEqualTo(600);
        await Assert.That(Create(retentionDays: 0, maxActiveResources: 0).AuditRetentionDays).IsEqualTo(0);
        await Assert.That(Create(retentionDays: 90, maxActiveResources: 500).MaxActiveResources).IsEqualTo(500);
    }

    [Test]
    public async Task Create_SnapshotsCollectionInputsAndHasValueEquality()
    {
        var deliveryTypes = new List<EventResourceDeliveryTypeEnum> { EventResourceDeliveryTypeEnum.StoredFile };
        var origins = new List<string> { "https://files.example.com" };
        var first = Create(deliveryTypes: deliveryTypes, origins: origins);
        var equal = Create(deliveryTypes: [EventResourceDeliveryTypeEnum.StoredFile], origins: ["https://files.example.com"]);

        deliveryTypes.Add(EventResourceDeliveryTypeEnum.ExternalLink);
        origins.Clear();

        await Assert.That(first.EnabledDeliveryTypes).IsEquivalentTo(new[] { EventResourceDeliveryTypeEnum.StoredFile });
        await Assert.That(first.ExternalOrigins).IsEquivalentTo(new[] { "https://files.example.com" });
        await Assert.That(first.Equals(equal)).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(equal.GetHashCode());
    }

    private static EventResourceGovernancePolicy Create(
        IEnumerable<EventResourceDeliveryTypeEnum>? deliveryTypes = null,
        IEnumerable<EventResourceAudienceKindEnum>? audiences = null,
        IEnumerable<string>? fileTypes = null,
        long maxUploadBytes = 1000,
        bool allowUnscanned = false,
        IEnumerable<string>? origins = null,
        int retentionDays = 30,
        int maxActiveResources = 100,
        long storageCeiling = 2000) =>
        EventResourceGovernancePolicy.Create(
            deliveryTypes ?? Enum.GetValues<EventResourceDeliveryTypeEnum>(),
            audiences ?? Enum.GetValues<EventResourceAudienceKindEnum>(),
            fileTypes ?? [Pdf],
            maxUploadBytes,
            allowUnscanned,
            origins ?? [],
            retentionDays,
            maxActiveResources,
            storageCeiling);
}
