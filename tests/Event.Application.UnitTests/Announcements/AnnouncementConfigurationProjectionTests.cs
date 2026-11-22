using System.Text.Json;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Application.UnitTests.Announcements;

public sealed class AnnouncementConfigurationProjectionTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid Edition = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid Epoch = Guid.Parse("00000000-0000-4000-8000-000000000003");

    [Test]
    public async Task ExportAndReimportKeepMaterialWithoutRuntimeTokensOrImplicitRedisplay()
    {
        var group = new AnnouncementGroup(Epoch, true, new(Edition, new("Notice", "Read", "/read")));
        var source = new AnnouncementSourceSnapshot(AnnouncementOwner.Instance, group, true);
        var exported = AnnouncementConfigurationProjection.Export(source);
        var imported = AnnouncementConfigurationProjection.Parse(AnnouncementOwner.Instance, exported);

        await Assert.That(exported.TryGetProperty("selectionEpoch", out _)).IsFalse();
        await Assert.That(exported.TryGetProperty("edition", out _)).IsFalse();
        await Assert.That(exported.GetProperty("redisplay").GetBoolean()).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(group, imported)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, group, true, imported, null, null))
            .IsEqualTo(new AnnouncementWrite(group, true));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InheritanceExportsAsInheritanceRatherThanInstanceMaterial(bool tombstone)
    {
        var owner = AnnouncementOwner.ForTenant(Tenant);
        var exported = AnnouncementConfigurationProjection.Export(
            new(owner, tombstone ? new AnnouncementGroup(Epoch, false, null) : null, false));

        await Assert.That(exported.GetProperty("operation").GetString()).IsEqualTo("inherit");
        await Assert.That(AnnouncementConfigurationProjection.Parse(owner, exported)
            is AnnouncementChange.RemoveOverride).IsTrue();
    }

    [Test]
    public async Task EmptyInstanceExportsAnExplicitDisableWithoutFabricatingMaterial()
    {
        var exported = AnnouncementConfigurationProjection.Export(new(AnnouncementOwner.Instance, null, false));
        var change = AnnouncementConfigurationProjection.Parse(AnnouncementOwner.Instance, exported);

        await Assert.That(exported.GetProperty("operation").GetString()).IsEqualTo("disable");
        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, null, false, change, null, null))
            .IsEqualTo(new AnnouncementWrite(null, false));
    }

    [Test]
    [Arguments("{\"operation\":\"publish\",\"enabled\":true,\"message\":\"Notice\",\"linkText\":\"\",\"linkUrl\":\"\",\"redisplay\":false,\"edition\":\"00000000-0000-4000-8000-000000000002\"}")]
    [Arguments("{\"operation\":\"publish\",\"enabled\":true,\"message\":\"Notice\",\"linkText\":\"\",\"redisplay\":false}")]
    [Arguments("{\"operation\":\"disable\",\"selectionEpoch\":\"00000000-0000-4000-8000-000000000003\"}")]
    [Arguments("{\"operation\":\"disable\",\"operation\":\"inherit\"}")]
    [Arguments("{\"operation\":\"inherit\"}")]
    public async Task ImportRejectsRuntimeTokensIncompleteMaterialAndInvalidInstanceRemoval(string json)
    {
        using var document = JsonDocument.Parse(json);
        await Assert.That(() => AnnouncementConfigurationProjection.Parse(
            AnnouncementOwner.Instance, document.RootElement)).Throws<ArgumentException>();
    }

    [Test]
    public async Task OnlyAnExplicitLockIntentChangesTheLock()
    {
        using var document = JsonDocument.Parse("{\"operation\":\"setLock\",\"isLocked\":false}");
        var change = AnnouncementConfigurationProjection.Parse(AnnouncementOwner.Instance, document.RootElement);
        var current = new AnnouncementGroup(Epoch, true, new(Edition, new("Notice", "", "")));
        var nextEpoch = Guid.Parse("00000000-0000-4000-8000-000000000004");

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, true, change, null, nextEpoch))
            .IsEqualTo(new AnnouncementWrite(new(nextEpoch, true, current.Publication), false));
    }

    [Test]
    public async Task ExportedLockMetadataCannotUnlockAPublication()
    {
        using var document = JsonDocument.Parse("""
            {"operation":"publish","enabled":true,"message":"Notice","linkText":"","linkUrl":"",
             "redisplay":false,"currentLock":false}
            """);
        var current = new AnnouncementGroup(Epoch, true, new(Edition, new("Notice", "", "")));
        var change = AnnouncementConfigurationProjection.Parse(AnnouncementOwner.Instance, document.RootElement);

        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, true, change, null, null))
            .IsEqualTo(new AnnouncementWrite(current, true));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreviewRetainsExplicitRedisplayEvenWhenExportedMaterialMatches(bool redisplay)
    {
        var source = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication] = new
            {
                operation = "publish", enabled = true, message = "Notice", linkText = "", linkUrl = "",
                redisplay, currentLock = false
            }
        });
        var target = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication] =
                AnnouncementConfigurationProjection.Export(new(AnnouncementOwner.Instance,
                    new(Epoch, true, new(Edition, new("Notice", "", ""))), false))
        });

        await Assert.That(Preview(source, target)).IsEqualTo(redisplay
            ? ConfigurationImportPreviewCategory.Changed : ConfigurationImportPreviewCategory.Unchanged);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OmittedAnnouncementIntentDoesNotHideUnrelatedSettingsChanges(bool changeOtherSetting)
    {
        var source = JsonSerializer.SerializeToElement(new { other = "original" });
        var target = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["other"] = changeOtherSetting ? "changed" : "original",
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication] = new { operation = "disable", currentLock = false }
        });

        await Assert.That(Preview(source, target)).IsEqualTo(changeOtherSetting
            ? ConfigurationImportPreviewCategory.Changed : ConfigurationImportPreviewCategory.Unchanged);
    }

    private static ConfigurationImportPreviewCategory Preview(JsonElement source, JsonElement target)
    {
        const string section = "instance.settings";
        var descriptor = ConfigurationPortabilityRegistry.Sections[section];
        ConfigurationImportSectionSnapshot Snapshot(JsonElement image) => new(section,
            ConfigurationImportDigest.ComputeBytes(Encoding.UTF8.GetBytes(image.GetRawText())),
            descriptor.PortabilityClass, descriptor.SupportsPreview, descriptor.SupportsDiff, false,
            settingsImage: image);
        var input = new ConfigurationImportPreviewInput(ConfigurationImportTarget.ForInstance(),
            new string('a', 64), new string('b', 64), [Snapshot(source)], [Snapshot(target)],
            [section, .. descriptor.Dependencies], [], ConfigurationImportApplyMode.PreviewOnly, [], [],
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return new ConfigurationImportPreviewComposer().Compose(input).Items.Single(item => item.SectionKey == section).Category;
    }

    [Test]
    public async Task AnnouncementMergePreservesSettingsNotPresentInTheArtifact()
    {
        var source = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication] = new { operation = "disable" }
        });
        var target = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication] = new { operation = "disable", currentLock = false },
            ["other"] = "preserved"
        });
        await Assert.That(Preview(source, target)).IsEqualTo(ConfigurationImportPreviewCategory.Unchanged);
    }
}
