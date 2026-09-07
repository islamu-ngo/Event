using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Recovery;
using Explore.Secrets.Configuration;
using Explore.Secrets.Validation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.UnitTests;

public sealed class TicketingRecoveryOperatorContractTests
{
    [Test]
    public async Task ValidEnabledConfigurationMeetsRecoveryAuthorityContract()
    {
        TicketingRecoveryOperatorOptions options = ValidOptions();

        ValidateOptionsResult result =
            new TicketingRecoveryOperatorOptionsValidator()
                .Validate(Options.DefaultName, options);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(options.DeclaredRpoMinutes)
            .IsLessThanOrEqualTo(15);
        await Assert.That(options.DeclaredRtoMinutes)
            .IsLessThanOrEqualTo(60);
        await Assert.That(options.ManifestSigningKeyReference)
            .IsEqualTo(
                SecretDefinitionRegistry.Keys.Ticketing
                    .RecoveryManifestHmacKey);
        await Assert.That(options.RetainedKeyVersions)
            .Contains(options.MinimumRetainedKeyVersion);
    }

    [Test]
    public async Task EnabledConfigurationRejectsMissingAuthorityAndUnsafeTargets()
    {
        var options = new TicketingRecoveryOperatorOptions
        {
            Enabled = true,
            ExpectedReleaseRevision = string.Empty,
            ExpectedSchemaRevision = string.Empty,
            MinimumRetainedKeyVersion = 0,
            MinimumAuthorityFloor = -1,
            MinimumProviderCursor = -1,
            MinimumIdempotencyFloor = -1,
            MinimumWorkerFence = -1,
            WarningOldestDueSeconds = 120,
            UnhealthyOldestDueSeconds = 60,
            BacklogThreshold = 0,
            DeclaredRpoMinutes = 16,
            DeclaredRtoMinutes = 61,
            ManifestSigningKeyReference = "wrong.key",
        };

        ValidateOptionsResult result =
            new TicketingRecoveryOperatorOptionsValidator()
                .Validate(Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).IsNotNull();
        await Assert.That(result.Failures!).Count()
            .IsGreaterThanOrEqualTo(7);
    }

    [Test]
    public async Task DisabledRecoveryControlsFailClosedWithoutDependencies()
    {
        var service = new TicketingRecoveryOperatorService(
            store: null!,
            scheduler: null!,
            secretResolver: null!,
            Options.Create(
                new TicketingRecoveryOperatorOptions
                {
                    Enabled = false,
                }),
            TimeProvider.System);
        Guid tenantId = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        Guid effectId = Guid.CreateVersion7();
        TicketingRecoveryManifest manifest =
            TicketingRecoveryManifest.Create(
                operationId,
                tenantId,
                "release",
                "schema",
                databaseCheckpoint: 1,
                objectCutoffUtc: DateTime.UtcNow,
                retainedKeyVersion: 1,
                authorityFloor: 1,
                providerCursor: 1,
                idempotencyFloor: 1,
                workerFence: 1,
                capabilityGeneration: 1,
                credentialGeneration: 1,
                digest: new string('0', 64));

        await Assert.That(
                await service.BeginRecoveryAsync(
                    manifest,
                    CancellationToken.None))
            .IsNull();
        await Assert.That(
                await service.StopSalesAsync(
                    tenantId,
                    operationId,
                    nextWorkerFence: 2,
                    CancellationToken.None))
            .IsFalse();
        await Assert.That(
                await service.PauseWorkersAsync(
                    tenantId,
                    operationId,
                    CancellationToken.None))
            .IsFalse();
        await Assert.That(
                await service.ReconcileAsync(
                    tenantId,
                    operationId,
                    CancellationToken.None))
            .IsNull();
        await Assert.That(
                await service.ResolveUnknownAsync(
                    tenantId,
                    effectId,
                    expectedFence: 1,
                    retry: true,
                    CancellationToken.None))
            .IsFalse();
        await Assert.That(
                await service.DeadLetterAsync(
                    tenantId,
                    effectId,
                    expectedFence: 1,
                    CancellationToken.None))
            .IsFalse();
        await Assert.That(
                await service.ReopenWorkersAsync(
                    tenantId,
                    operationId,
                    CancellationToken.None))
            .IsFalse();
        await Assert.That(
                await service.ReopenSalesAsync(
                    tenantId,
                    operationId,
                    CancellationToken.None))
            .IsFalse();
    }

    [Test]
    public async Task DisabledHealthIsSafeAndFixedCardinality()
    {
        var healthCheck = new TicketingRecoveryHealthCheck(
            scopeFactory: null!,
            Options.Create(
                new TicketingRecoveryOperatorOptions
                {
                    Enabled = false,
                }),
            TimeProvider.System);

        HealthCheckResult result =
            await healthCheck.CheckHealthAsync(
                new HealthCheckContext(),
                CancellationToken.None);

        await Assert.That(result.Status)
            .IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data.Keys)
            .IsEquivalentTo(
                TicketingRecoveryHealthCheck.DataKeys);
        await Assert.That(result.Data["status"])
            .IsEqualTo("disabled");
        await Assert.That(
                result.Data.Keys.Any(
                    key => ForbiddenHealthFragments.Any(
                        fragment => key.Contains(
                            fragment,
                            StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
    }

    private static readonly string[] ForbiddenHealthFragments =
    [
        "tenant",
        "event",
        "order",
        "ticket",
        "actor",
        "user",
        "provider",
        "capability",
        "digest",
        "amount",
    ];

    private static TicketingRecoveryOperatorOptions ValidOptions() =>
        new()
        {
            Enabled = true,
            ExpectedReleaseRevision = "release-2026-08-30",
            ExpectedSchemaRevision = "schema-2026-08-30",
            MinimumRetainedKeyVersion = 2,
            MinimumAuthorityFloor = 10,
            MinimumProviderCursor = 20,
            MinimumIdempotencyFloor = 30,
            MinimumWorkerFence = 40,
            WarningOldestDueSeconds = 60,
            UnhealthyOldestDueSeconds = 120,
            BacklogThreshold = 100,
            DeclaredRpoMinutes = 15,
            DeclaredRtoMinutes = 60,
            ManifestSigningKeyReference =
                SecretDefinitionRegistry.Keys.Ticketing
                    .RecoveryManifestHmacKey,
            RetainedKeyVersions = [1, 2],
        };
}
