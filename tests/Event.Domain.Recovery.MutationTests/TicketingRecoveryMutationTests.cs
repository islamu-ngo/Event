using Explore.Domain;

namespace Explore.Domain.Recovery.MutationTests;

public sealed class TicketingRecoveryMutationTests
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ManifestPreservesEveryConsistencyFact()
    {
        Guid operationId = NewId();
        Guid tenantId = NewId();
        TicketingRecoveryManifest manifest = Manifest(operationId, tenantId);

        await Assert.That(manifest.OperationId).IsEqualTo(operationId);
        await Assert.That(manifest.TenantId).IsEqualTo(tenantId);
        await Assert.That(manifest.ReleaseRevision).IsEqualTo("release-8");
        await Assert.That(manifest.SchemaRevision).IsEqualTo("schema-8");
        await Assert.That(manifest.DatabaseCheckpoint).IsEqualTo(70L);
        await Assert.That(manifest.ObjectCutoffUtc).IsEqualTo(UtcNow);
        await Assert.That(manifest.RetainedKeyVersion).IsEqualTo(3);
        await Assert.That(manifest.AuthorityFloor).IsEqualTo(80L);
        await Assert.That(manifest.ProviderCursor).IsEqualTo(90L);
        await Assert.That(manifest.IdempotencyFloor).IsEqualTo(100L);
        await Assert.That(manifest.WorkerFence).IsEqualTo(110L);
        await Assert.That(manifest.CapabilityGeneration).IsEqualTo(5);
        await Assert.That(manifest.CredentialGeneration).IsEqualTo(8);
        await Assert.That(manifest.Digest).IsEqualTo(new string('a', 64));
    }

    [Test]
    public async Task ManifestRejectsMalformedIdentityFloorTimeAndDigest()
    {
        await Assert.That(() => Manifest(Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => Manifest(tenantId: Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => Manifest(release: " ")).Throws<ArgumentException>();
        await Assert.That(() => Manifest(schema: " ")).Throws<ArgumentException>();
        await Assert.That(() => Manifest(databaseCheckpoint: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(retainedKeyVersion: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(authorityFloor: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(providerCursor: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(idempotencyFloor: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(workerFence: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(capabilityGeneration: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(credentialGeneration: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Manifest(digest: "bad"))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(digest: new string('a', 63) + "g"))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(operationId: Guid.NewGuid()))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(tenantId: Guid.NewGuid()))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(release: new string('r', 101)))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(schema: new string('s', 101)))
            .Throws<ArgumentException>();
        await Assert.That(() => Manifest(
                cutoff: DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        await Assert.That(Manifest(release: "r").ReleaseRevision)
            .IsEqualTo("r");
        await Assert.That(Manifest(schema: new string('s', 100)).SchemaRevision.Length)
            .IsEqualTo(100);
    }

    [Test]
    public async Task ValidationReportsEveryClosedMismatchWithoutAdvancing()
    {
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "other", "schema-8", 3, 80, 90, 100, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.ReleaseMismatch);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "other", 3, 80, 90, 100, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.SchemaMismatch);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "schema-8", 4, 80, 90, 100, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.MissingKey);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "schema-8", 3, 81, 90, 100, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.StaleAuthority);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "schema-8", 3, 80, 91, 100, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.StaleProviderCursor);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "schema-8", 3, 80, 90, 101, 110, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.MissingIdempotency);
        await AssertOutcome(
            checkpoint => checkpoint.Validate(
                "release-8", "schema-8", 3, 80, 90, 100, 111, UtcNow.AddMinutes(1)),
            TicketingRecoveryValidationOutcome.StaleWorkerFence);
    }

    [Test]
    public async Task ValidatedRecoveryRequiresStrictGenerationAndFenceAdvance()
    {
        TicketingRecoveryCheckpoint checkpoint = Begin();
        await Assert.That(Validate(checkpoint))
            .IsEqualTo(TicketingRecoveryValidationOutcome.Validated);
        await Assert.That(checkpoint.Status).IsEqualTo(TicketingRecoveryStatus.Validated);
        await Assert.That(checkpoint.ValidatedAt).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(Validate(checkpoint))
            .IsEqualTo(TicketingRecoveryValidationOutcome.Validated);
        Guid validatedStamp = checkpoint.ConcurrencyStamp;
        await Assert.That(checkpoint.TryRotateBearerAuthority(
                5, 9, 111, UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(checkpoint.TryRotateBearerAuthority(
                6, 8, 111, UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(checkpoint.TryRotateBearerAuthority(
                6, 9, 110, UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(checkpoint.TryRotateBearerAuthority(
                6, 9, 111, UtcNow.AddMinutes(2)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.AuthorityRotated);
        await Assert.That(checkpoint.CapabilityGeneration).IsEqualTo(6);
        await Assert.That(checkpoint.CredentialGeneration).IsEqualTo(9);
        await Assert.That(checkpoint.WorkerFence).IsEqualTo(111L);
        await Assert.That(checkpoint.AuthorityRotatedAt)
            .IsEqualTo(UtcNow.AddMinutes(2));
        await Assert.That(checkpoint.UpdatedAt).IsEqualTo(UtcNow.AddMinutes(2));
        await Assert.That(checkpoint.ConcurrencyStamp).IsNotEqualTo(validatedStamp);
    }

    [Test]
    public async Task WorkersAndSalesOpenInExactOrderAndFence()
    {
        TicketingRecoveryCheckpoint checkpoint = Rotated();
        await Assert.That(checkpoint.TryOpenWorkers(110, UtcNow.AddMinutes(3)))
            .IsFalse();
        await Assert.That(checkpoint.TryOpenSales(UtcNow.AddMinutes(3)))
            .IsFalse();
        await Assert.That(checkpoint.TryOpenWorkers(111, UtcNow.AddMinutes(3)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.WorkersOpen);
        await Assert.That(checkpoint.WorkersOpenedAt)
            .IsEqualTo(UtcNow.AddMinutes(3));
        await Assert.That(checkpoint.TryOpenWorkers(111, UtcNow.AddMinutes(4)))
            .IsFalse();
        await Assert.That(checkpoint.TryOpenSales(UtcNow.AddMinutes(4)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.SalesOpen);
        await Assert.That(checkpoint.SalesOpenedAt).IsEqualTo(UtcNow.AddMinutes(4));
        await Assert.That(checkpoint.UpdatedAt).IsEqualTo(UtcNow.AddMinutes(4));
    }

    [Test]
    public async Task StopAndPauseReturnToClosedStatesMonotonically()
    {
        TicketingRecoveryCheckpoint checkpoint = Rotated();
        await Assert.That(checkpoint.TryOpenWorkers(111, UtcNow.AddMinutes(3)))
            .IsTrue();
        await Assert.That(checkpoint.TryOpenSales(UtcNow.AddMinutes(4)))
            .IsTrue();
        await Assert.That(checkpoint.PauseWorkers(UtcNow.AddMinutes(5)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.AuthorityRotated);
        await Assert.That(checkpoint.PauseWorkers(UtcNow.AddMinutes(6)))
            .IsFalse();
        await Assert.That(checkpoint.StopSales(111, UtcNow.AddMinutes(6)))
            .IsFalse();
        await Assert.That(checkpoint.StopSales(112, UtcNow.AddMinutes(6)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.RecoveryOnly);
        await Assert.That(checkpoint.WorkerFence).IsEqualTo(112L);
        await Assert.That(checkpoint.ValidatedAt).IsNull();
        await Assert.That(checkpoint.AuthorityRotatedAt).IsNull();
        await Assert.That(checkpoint.WorkersOpenedAt).IsNull();
        await Assert.That(checkpoint.SalesOpenedAt).IsNull();
    }

    [Test]
    public async Task FailureAndReissueIntentRemainClosedAndDigestFree()
    {
        TicketingRecoveryCheckpoint checkpoint = Begin();
        checkpoint.Fail(" restore_failed ", UtcNow.AddMinutes(1));
        await Assert.That(checkpoint.Status).IsEqualTo(TicketingRecoveryStatus.Failed);
        await Assert.That(checkpoint.FailureCode).IsEqualTo("RESTORE_FAILED");
        await Assert.That(checkpoint.UpdatedAt).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(Validate(checkpoint))
            .IsEqualTo(TicketingRecoveryValidationOutcome.StaleAuthority);
        await Assert.That(checkpoint.StopSales(111, UtcNow.AddMinutes(2))).IsFalse();
        await Assert.That(() => Begin().Fail("", UtcNow.AddMinutes(1)))
            .Throws<ArgumentException>();
        await Assert.That(() => Begin().Fail(
                new string('f', 65),
                UtcNow.AddMinutes(1)))
            .Throws<ArgumentException>();
        TicketingRecoveryCheckpoint boundary = Begin();
        boundary.Fail(new string('f', 64), UtcNow.AddMinutes(1));
        await Assert.That(boundary.FailureCode!.Length).IsEqualTo(64);

        Guid tenantId = NewId();
        Guid operationId = NewId();
        Guid ticketId = NewId();
        TicketingRecoveryReissueIntent intent =
            TicketingRecoveryReissueIntent.Create(
                tenantId,
                operationId,
                ticketId,
                9,
                UtcNow);
        await Assert.That(intent.TenantId).IsEqualTo(tenantId);
        await Assert.That(intent.RecoveryOperationId).IsEqualTo(operationId);
        await Assert.That(intent.AdmissionTicketId).IsEqualTo(ticketId);
        await Assert.That(intent.RequiredCredentialGeneration).IsEqualTo(9);
        await Assert.That(intent.Status).IsEqualTo(TicketingRecoveryReissueStatus.Pending);
        await Assert.That(intent.Id.Version).IsEqualTo(7);
        await Assert.That(intent.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(intent.CreatedAt).IsEqualTo(UtcNow);
    }

    private static async Task AssertOutcome(
        Func<TicketingRecoveryCheckpoint, TicketingRecoveryValidationOutcome> validate,
        TicketingRecoveryValidationOutcome expected)
    {
        TicketingRecoveryCheckpoint checkpoint = Begin();
        await Assert.That(validate(checkpoint)).IsEqualTo(expected);
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.RecoveryOnly);
        await Assert.That(checkpoint.ValidatedAt).IsNull();
    }

    private static TicketingRecoveryCheckpoint Begin() =>
        TicketingRecoveryCheckpoint.Begin(Manifest(), UtcNow);

    private static TicketingRecoveryCheckpoint Rotated()
    {
        TicketingRecoveryCheckpoint checkpoint = Begin();
        _ = Validate(checkpoint);
        _ = checkpoint.TryRotateBearerAuthority(
            6,
            9,
            111,
            UtcNow.AddMinutes(2));
        return checkpoint;
    }

    private static TicketingRecoveryValidationOutcome Validate(
        TicketingRecoveryCheckpoint checkpoint) =>
        checkpoint.Validate(
            "release-8",
            "schema-8",
            3,
            80,
            90,
            100,
            110,
            UtcNow.AddMinutes(1));

    private static TicketingRecoveryManifest Manifest(
        Guid? operationId = null,
        Guid? tenantId = null,
        string release = "release-8",
        string schema = "schema-8",
        long databaseCheckpoint = 70,
        DateTime? cutoff = null,
        int retainedKeyVersion = 3,
        long authorityFloor = 80,
        long providerCursor = 90,
        long idempotencyFloor = 100,
        long workerFence = 110,
        int capabilityGeneration = 5,
        int credentialGeneration = 8,
        string? digest = null) =>
        TicketingRecoveryManifest.Create(
            operationId ?? NewId(),
            tenantId ?? NewId(),
            release,
            schema,
            databaseCheckpoint,
            cutoff ?? UtcNow,
            retainedKeyVersion,
            authorityFloor,
            providerCursor,
            idempotencyFloor,
            workerFence,
            capabilityGeneration,
            credentialGeneration,
            digest ?? new string('a', 64));

    private static Guid NewId() => Guid.CreateVersion7();
}
