namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed class ConfigurationImportSessionBehaviorTests
{
    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Session_WrongTargetAndWrongTokenFailClosed()
    {
        ConfigurationImportTarget target =
            ConfigurationImportTarget.ForTenant(Guid.CreateVersion7());
        ConfigurationImportSession session = Session(target);

        ConfigurationImportSessionException targetFailure =
            await Assert.That(() => session.AuthorizePreview(
                    ConfigurationImportTarget.ForTenant(Guid.CreateVersion7()),
                    Digest("token"),
                    OccurredAt.AddMinutes(1)))
                .Throws<ConfigurationImportSessionException>();
        ConfigurationImportSessionException tokenFailure =
            await Assert.That(() => session.AuthorizePreview(
                    target,
                    Digest("different-token"),
                    OccurredAt.AddMinutes(1)))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(targetFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.TargetMismatch);
        await Assert.That(tokenFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.TokenInvalid);
    }

    [Test]
    public async Task Session_ExpiryAndCancellationRejectFurtherPreview()
    {
        ConfigurationImportTarget target =
            ConfigurationImportTarget.ForInstance();
        ConfigurationImportSession expired = Session(target);
        ConfigurationImportSessionException expiryFailure =
            await Assert.That(() => expired.AuthorizePreview(
                    target,
                    Digest("token"),
                    OccurredAt.AddMinutes(30)))
                .Throws<ConfigurationImportSessionException>();
        ConfigurationImportSession cancelled = Session(target);
        cancelled.Cancel(OccurredAt.AddMinutes(1));
        ConfigurationImportSessionException cancellationFailure =
            await Assert.That(() => cancelled.AuthorizePreview(
                    target,
                    Digest("token"),
                    OccurredAt.AddMinutes(2)))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(expiryFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.Expired);
        await Assert.That(cancellationFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.Cancelled);
    }

    [Test]
    public async Task Session_ConsumptionIsOneTimeAndReplayProtected()
    {
        ConfigurationImportTarget target =
            ConfigurationImportTarget.ForInstance();
        ConfigurationImportSession session = Session(target);
        ConfigurationImportPreviewBinding binding = Binding(target);
        session.MarkPreviewReady(binding, OccurredAt.AddMinutes(1));
        session.Consume(
            binding,
            target,
            Digest("token"),
            OccurredAt.AddMinutes(2));

        ConfigurationImportSessionException failure =
            await Assert.That(() => session.Consume(
                    binding,
                    target,
                    Digest("token"),
                    OccurredAt.AddMinutes(3)))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(session.State)
            .IsEqualTo(ConfigurationImportSessionState.Consumed);
        await Assert.That(failure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.Replayed);
    }

    [Test]
    public async Task Session_ChangedRevisionInvalidatesPersistedPreview()
    {
        ConfigurationImportTarget target =
            ConfigurationImportTarget.ForInstance();
        ConfigurationImportSession session = Session(target);
        ConfigurationImportPreviewBinding current = Binding(target);
        session.MarkPreviewReady(current, OccurredAt.AddMinutes(1));
        var stale = new ConfigurationImportPreviewBinding(
            target,
            current.ArtifactDigest,
            Digest("changed-target-revision"),
            current.SelectedSectionsDigest,
            current.MappingDigest,
            current.ApplyMode,
            current.RequiredApprovalDigest,
            current.ExpiresAt);

        ConfigurationImportSessionException failure =
            await Assert.That(() => session.Consume(
                    stale,
                    target,
                    Digest("token"),
                    OccurredAt.AddMinutes(2)))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(failure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.StalePreview);
        await Assert.That(session.State)
            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
    }

    [Test]
    public async Task Composer_ClassifiesSupportedAndUnavailableOutcomes()
    {
        string same = Digest("same");
        var source = new List<ConfigurationImportSectionSnapshot>
        {
            Section("instance.settings", Digest("source-changed")),
            Section("instance.documents", same),
            Section("instance.legal_documents", Digest("skipped")),
            Section("tenant.footer", Digest("skipped")),
            Section(
                "tenant.lookups",
                Digest("mapped-source"),
                ConfigurationPortabilityClass.PortableWithMapping,
                stableMappingIdentity: "source-machine-code"),
            Section(
                "tenant.custom_property_definitions",
                Digest("blocking"),
                ConfigurationPortabilityClass.PortableWithMapping,
                blockingReasonCode: "configuration_import_blocking_test"),
            Section(
                "tenant.navigation",
                Digest("warning"),
                warningReasonCode: "configuration_import_warning_test"),
            Section(
                "excluded.secrets",
                Digest("omitted"),
                ConfigurationPortabilityClass.Secret),
            Section(
                "tenant.modules",
                Digest("external"),
                requiresExternalSetup: true)
        };
        ConfigurationImportSectionSnapshot[] targetSections =
        [
            Section("instance.settings", Digest("target-changed")),
            Section("instance.documents", same),
            Section(
                "tenant.lookups",
                Digest("mapped-target"),
                ConfigurationPortabilityClass.PortableWithMapping,
                stableMappingIdentity: "target-machine-code")
        ];
        string[] selected =
        [
            "excluded.secrets",
            "instance.documents",
            "instance.settings",
            "tenant.custom_property_definitions",
            "tenant.lookups",
            "tenant.modules",
            "tenant.navigation"
        ];
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForInstance(),
            Digest("artifact"),
            Digest("target-revision"),
            source,
            targetSections,
            selected,
            [
                new KeyValuePair<string, string>(
                    "source-machine-code",
                    "target-machine-code")
            ],
            ConfigurationImportApplyMode.ApplySelected,
            ["approval-code"],
            ["approval-code"],
            OccurredAt.AddMinutes(20));

        ConfigurationImportPreview preview =
            new ConfigurationImportPreviewComposer().Compose(input);

        await Assert.That(preview.Items.Select(item => item.Category).Distinct())
            .Contains(ConfigurationImportPreviewCategory.Changed);
        await Assert.That(preview.Items.Select(item => item.Category).Distinct())
            .Contains(ConfigurationImportPreviewCategory.Unchanged);
        await Assert.That(preview.Items.Select(item => item.Category).Distinct())
            .Contains(ConfigurationImportPreviewCategory.Skipped);
        await Assert.That(preview.Items.Select(item => item.Category).Distinct())
            .Contains(ConfigurationImportPreviewCategory.Blocking);
        await Assert.That(preview.Items.Select(item => item.Category).Distinct())
            .Contains(ConfigurationImportPreviewCategory.Omitted);
        await Assert.That(preview.IsApplyReady).IsFalse();
    }

    [Test]
    public async Task PreviewInput_SnapshotsMutableCollections()
    {
        var source = new List<ConfigurationImportSectionSnapshot>
        {
            Section("settings", Digest("settings"))
        };
        var selected = new List<string> { "settings" };
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        var approvals = new List<string> { "approval" };
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForInstance(),
            Digest("artifact"),
            Digest("target"),
            source,
            [],
            selected,
            mappings,
            ConfigurationImportApplyMode.ApplySelected,
            approvals,
            approvals,
            OccurredAt.AddMinutes(20));

        source.Clear();
        selected.Clear();
        mappings["source"] = "target";
        approvals.Clear();

        await Assert.That(input.SourceSections).HasCount(1);
        await Assert.That(input.SelectedSectionKeys).Contains("settings");
        await Assert.That(input.Mappings).IsEmpty();
        await Assert.That(input.RequiredApprovalCodes).Contains("approval");
    }

    [Test]
    public async Task Composer_UnknownSectionAndMissingApprovalBlockApply()
    {
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForInstance(),
            Digest("artifact"),
            Digest("target"),
            [Section("unknown.section", Digest("unknown"))],
            [],
            ["unknown.section"],
            [],
            ConfigurationImportApplyMode.ApplySelected,
            ["enhanced-approval"],
            [],
            OccurredAt.AddMinutes(20));

        ConfigurationImportPreview preview =
            new ConfigurationImportPreviewComposer().Compose(input);

        await Assert.That(preview.IsApplyReady).IsFalse();
        await Assert.That(preview.Items.Select(item => item.ReasonCode))
            .Contains("configuration_import_section_unknown");
        await Assert.That(preview.Items.Select(item => item.ReasonCode))
            .Contains("configuration_import_required_approval_missing");
    }

    [Test]
    public async Task Observability_RejectsUnboundedOrValueBearingCodes()
    {
        await Assert.That(() => new ConfigurationImportObservabilityEvent(
                "instance",
                "manifest",
                "secret@example.test\nraw",
                1,
                0,
                2))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task MappingIdentity_RejectsLocalizedDisplayNames()
    {
        await Assert.That(() => Section(
                "tenant.lookups",
                Digest("lookup"),
                ConfigurationPortabilityClass.PortableWithMapping,
                stableMappingIdentity: "اسم محلي"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Composer_ReportsEveryRegistrySectionForInstanceCoverage()
    {
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForInstance(),
            Digest("artifact"),
            Digest("target"),
            [],
            [],
            [],
            [],
            ConfigurationImportApplyMode.PreviewOnly,
            [],
            [],
            OccurredAt.AddMinutes(20));

        ConfigurationImportPreview preview =
            new ConfigurationImportPreviewComposer().Compose(input);

        string[] missing = ConfigurationPortabilityRegistry.Sections.Keys
            .Except(
                preview.Items.Select(item => item.SectionKey),
                StringComparer.Ordinal)
            .ToArray();
        await Assert.That(missing).IsEmpty();
        await Assert.That(preview.Items.All(item =>
                item.Category == ConfigurationImportPreviewCategory.Omitted))
            .IsTrue();
    }

    private static ConfigurationImportSession Session(
        ConfigurationImportTarget target)
    {
        var artifact = new ConfigurationImportArtifactReference(
            new ConfigurationImportArtifactHandle(Guid.CreateVersion7()),
            Digest("artifact"),
            128,
            OccurredAt.AddHours(1));
        return ConfigurationImportSession.Create(
            Guid.CreateVersion7(),
            target,
            artifact,
            Digest("token"),
            OccurredAt,
            TimeSpan.FromMinutes(30));
    }

    private static ConfigurationImportPreviewBinding Binding(
        ConfigurationImportTarget target) =>
        new(
            target,
            Digest("artifact"),
            Digest("target-revision"),
            Digest("selected"),
            Digest("mapping"),
            ConfigurationImportApplyMode.ApplySelected,
            Digest("approval"),
            OccurredAt.AddMinutes(20));

    private static ConfigurationImportSectionSnapshot Section(
        string key,
        string digest,
        ConfigurationPortabilityClass portabilityClass =
            ConfigurationPortabilityClass.Portable,
        bool requiresExternalSetup = false,
        string? stableMappingIdentity = null,
        string? blockingReasonCode = null,
        string? warningReasonCode = null) =>
        new(
            key,
            digest,
            portabilityClass,
            supportsPreview: true,
            supportsDiff: true,
            requiresExternalSetup,
            stableMappingIdentity,
            blockingReasonCode,
            warningReasonCode);

    private static string Digest(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
