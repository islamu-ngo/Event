namespace Event.Domain.UnitTests.ConfigurationManifest;

public sealed class ConfigurationManifestAuditTests
{
    private static readonly DateTime StartedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = StartedAt.AddSeconds(1);

    [Test]
    public async Task CreateApplied_UsesUuidV7AndAcceptsOnlyCompleteCounts()
    {
        ConfigurationManifestOperation operation = CreateApplied(requested: 2, created: 1, skipped: 1);

        await Assert.That(operation.Id.Version).IsEqualTo(7);
        await Assert.That(operation.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
        await Assert.That(operation.InstanceSectionDigest)
            .IsEqualTo(new string('c', 64));
        await Assert.That(operation.BootstrapGeneration).IsEqualTo(1);
        await Assert.That(() => CreateApplied(requested: 2, created: 1, skipped: 0))
            .Throws<ArgumentException>();
        await Assert.That(() => ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap,
                operation.ApiVersion,
                operation.Kind,
                operation.ManifestName,
                operation.Digest,
                ConfigurationManifestOperationStatus.Applied,
                requestedTenantCount: 1,
                createdTenantCount: 1,
                skippedExistingTenantCount: 0,
                failedTenantCount: 0,
                reasonCode: null,
                reason: null,
                StartedAt,
                CompletedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateValidated_RequiresValidateOnlyAndNoMutationCounts()
    {
        ConfigurationManifestOperation validated = ConfigurationManifestOperation.Create(
            ConfigurationManifestAuditMode.ValidateOnly,
            "configuration.islamu.org/v1alpha2",
            "ConfigurationManifest",
            "production",
            new string('a', 64),
            ConfigurationManifestOperationStatus.Validated,
            requestedTenantCount: 3,
            createdTenantCount: 0,
            skippedExistingTenantCount: 0,
            failedTenantCount: 0,
            reasonCode: null,
            reason: null,
            StartedAt,
            CompletedAt);

        await Assert.That(validated.RequestedTenantCount).IsEqualTo(3);
        await Assert.That(() => ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap,
                validated.ApiVersion,
                validated.Kind,
                validated.ManifestName,
                validated.Digest,
                ConfigurationManifestOperationStatus.Validated,
                3,
                0,
                0,
                0,
                null,
                null,
                StartedAt,
                CompletedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateFailed_RequiresSafeReasonAndCannotClaimCommittedChanges()
    {
        await Assert.That(() => ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap,
                "configuration.islamu.org/v1alpha2",
                "ConfigurationManifest",
                "production",
                new string('b', 64),
                ConfigurationManifestOperationStatus.Failed,
                requestedTenantCount: 2,
                createdTenantCount: 1,
                skippedExistingTenantCount: 0,
                failedTenantCount: 1,
                reasonCode: "configuration_manifest_apply_failed",
                reason: "The manifest could not be applied.",
                StartedAt,
                CompletedAt))
            .Throws<ArgumentException>();

        await Assert.That(() => ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap,
                "configuration.islamu.org/v1alpha2",
                "ConfigurationManifest",
                "production",
                new string('b', 64),
                ConfigurationManifestOperationStatus.Failed,
                2,
                0,
                0,
                1,
                null,
                null,
                StartedAt,
                CompletedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Create_RejectsMalformedDigestAndNonUtcOrReversedTimestamps()
    {
        await Assert.That(() => CreateApplied(1, 1, 0, digest: new string('A', 64)))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateApplied(
                1,
                1,
                0,
                startedAt: DateTime.SpecifyKind(StartedAt, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(() => CreateApplied(
                1,
                1,
                0,
                startedAt: CompletedAt,
                completedAt: StartedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TenantResult_SortsDistinctKeysAndSkippedExistingRejectsChanges()
    {
        ConfigurationManifestTenantResult created = ConfigurationManifestTenantResult.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ConfigurationManifestTenantResultStatus.Created,
            ["z.key", "a.key", "z.key"],
            ["branding"],
            CompletedAt);

        await Assert.That(created.Id.Version).IsEqualTo(7);
        await Assert.That(created.ChangedSettingKeyNames).IsEquivalentTo(["a.key", "z.key"]);
        await Assert.That(created.ChangedDocumentKeyNames).IsEquivalentTo(["branding"]);

        await Assert.That(() => ConfigurationManifestTenantResult.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                ConfigurationManifestTenantResultStatus.SkippedExisting,
                ["event_reporting.intake_enabled"],
                [],
                CompletedAt))
            .Throws<ArgumentException>();
    }

    private static ConfigurationManifestOperation CreateApplied(
        int requested,
        int created,
        int skipped,
        string? digest = null,
        DateTime? startedAt = null,
        DateTime? completedAt = null) =>
        ConfigurationManifestOperation.Create(
            ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2",
            "ConfigurationManifest",
            "production",
            digest ?? new string('a', 64),
            ConfigurationManifestOperationStatus.Applied,
            requested,
            created,
            skipped,
            failedTenantCount: 0,
            reasonCode: null,
            reason: null,
            startedAt ?? StartedAt,
            completedAt ?? CompletedAt,
            instanceSectionDigest: new string('c', 64),
            bootstrapGeneration: 1);
}
