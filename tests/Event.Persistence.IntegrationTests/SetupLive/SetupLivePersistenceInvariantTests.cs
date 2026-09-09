namespace Event.Persistence.IntegrationTests.SetupLive;

using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Application.Features.SetupLive;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.SetupLive;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.SetupLive;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class SetupLivePersistenceInvariantTests(
    PostgreSqlContainerFixture fixture)
{
    private const string ClaimReplayConstraint =
        "ix_setup_enrollment_issuance_claims_tenant_id_operation_key";
    private const string OperationReplayConstraint =
        "ix_setup_secret_binding_operations_tenant_id_operation_key";
    private const string ClaimEnrollmentConstraint =
        "fk_setup_enrollment_issuance_claims_setup_target_e_73846d3daed1";
    private const string OperationEnrollmentConstraint =
        "fk_setup_secret_binding_operations_setup_target_en_ff0a38ad8625";
    private static readonly DateTime Now =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TenantFilterFailsClosedForEverySetupLiveEntity()
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        SetupTargetEnrollment enrollmentA = Enrollment(tenantA);
        SetupTargetEnrollment enrollmentB = Enrollment(tenantB);
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(Tenant(tenantA, "setup-a"), Tenant(tenantB, "setup-b"));
            seed.Set<SetupTargetEnrollment>().AddRange(enrollmentA, enrollmentB);
            seed.Set<SetupEnrollmentIssuanceClaim>().AddRange(
                Claim(enrollmentA, Guid.CreateVersion7()),
                Claim(enrollmentB, Guid.CreateVersion7()));
            seed.Set<SetupSecretBindingOperation>().AddRange(
                Operation(enrollmentA, Guid.CreateVersion7()),
                Operation(enrollmentB, Guid.CreateVersion7()));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext tenantContext =
            fixture.CreateTenantFilteredDbContext(new TenantContext(tenantA));
        await using ExploreDbContext missingContext =
            fixture.CreateTenantFilteredDbContext();

        SetupTargetEnrollment[] visibleEnrollments = await tenantContext
            .Set<SetupTargetEnrollment>().AsNoTracking().ToArrayAsync();
        SetupEnrollmentIssuanceClaim[] visibleClaims = await tenantContext
            .Set<SetupEnrollmentIssuanceClaim>().AsNoTracking().ToArrayAsync();
        SetupSecretBindingOperation[] visibleOperations = await tenantContext
            .Set<SetupSecretBindingOperation>().AsNoTracking().ToArrayAsync();
        await Assert.That(visibleEnrollments).Count().IsEqualTo(1);
        await Assert.That(visibleClaims).Count().IsEqualTo(1);
        await Assert.That(visibleOperations).Count().IsEqualTo(1);
        await Assert.That(visibleEnrollments[0].TenantId).IsEqualTo(tenantA);
        await Assert.That(visibleClaims[0].TenantId).IsEqualTo(tenantA);
        await Assert.That(visibleClaims[0].EnrollmentId).IsEqualTo(enrollmentA.Id);
        await Assert.That(visibleOperations[0].TenantId).IsEqualTo(tenantA);
        await Assert.That(visibleOperations[0].EnrollmentId).IsEqualTo(enrollmentA.Id);
        await Assert.That(await missingContext.Set<SetupTargetEnrollment>().CountAsync())
            .IsEqualTo(0);
        await Assert.That(await missingContext.Set<SetupEnrollmentIssuanceClaim>().CountAsync())
            .IsEqualTo(0);
        await Assert.That(await missingContext.Set<SetupSecretBindingOperation>().CountAsync())
            .IsEqualTo(0);
    }

    [Test]
    public async Task ReplayKeysRaceAtTheDatabaseAndRemainTenantScoped()
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        SetupTargetEnrollment enrollmentA = Enrollment(tenantA);
        SetupTargetEnrollment secondEnrollmentA = Enrollment(tenantA);
        SetupTargetEnrollment enrollmentB = Enrollment(tenantB);
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(Tenant(tenantA, "replay-a"), Tenant(tenantB, "replay-b"));
            seed.Set<SetupTargetEnrollment>().AddRange(
                enrollmentA,
                secondEnrollmentA,
                enrollmentB);
            await seed.SaveChangesAsync();
        }

        Guid operationKey = Guid.CreateVersion7();
        await AssertUniqueRaceAsync(
            context => context.Set<SetupEnrollmentIssuanceClaim>().Add(
                Claim(enrollmentA, operationKey)),
            context => context.Set<SetupEnrollmentIssuanceClaim>().Add(
                Claim(secondEnrollmentA, operationKey)),
            ClaimReplayConstraint);
        await AssertUniqueRaceAsync(
            context => context.Set<SetupSecretBindingOperation>().Add(
                Operation(enrollmentA, operationKey)),
            context => context.Set<SetupSecretBindingOperation>().Add(
                Operation(secondEnrollmentA, operationKey)),
            OperationReplayConstraint);

        await using ExploreDbContext otherTenant = fixture.CreateDbContext();
        otherTenant.Set<SetupEnrollmentIssuanceClaim>().Add(
            Claim(enrollmentB, operationKey));
        otherTenant.Set<SetupSecretBindingOperation>().Add(
            Operation(enrollmentB, operationKey));
        await otherTenant.SaveChangesAsync();
        otherTenant.ChangeTracker.Clear();
        await Assert.That(await otherTenant.Set<SetupEnrollmentIssuanceClaim>()
            .CountAsync(claim => claim.OperationKey == operationKey)).IsEqualTo(2);
        await Assert.That(await otherTenant.Set<SetupSecretBindingOperation>()
            .CountAsync(operation => operation.OperationKey == operationKey)).IsEqualTo(2);
    }

    [Test]
    public async Task ConcurrentApplicationIssuanceSettlesAsOneWinnerAndOneValueFreeDuplicate()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        Guid operationKey = Guid.CreateVersion7();
        var request = new CreateSetupTargetEnrollmentRequest
        {
            ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
            RequestedScopes = [SetupEnrollmentScope.TargetRead]
        };
        var gate = new ClaimReadRendezvousInterceptor();

        await using ExploreDbContext firstContext = fixture.CreateDbContext(gate);
        await using ExploreDbContext secondContext = fixture.CreateDbContext(gate);
        SetupLiveApplicationService first = ApplicationService(
            firstContext, tenantId);
        SetupLiveApplicationService second = ApplicationService(
            secondContext, tenantId);

        SetupLiveEnrollmentResult[] results = await Task.WhenAll(
                first.CreateAsync(
                    tenantId, userId, operationKey, request, CancellationToken.None),
                second.CreateAsync(
                    tenantId, userId, operationKey, request, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(results.Select(result => result.Status))
            .IsEquivalentTo(
            [
                SetupLiveApplicationStatus.Created,
                SetupLiveApplicationStatus.Duplicate
            ]);
        SetupLiveEnrollmentResult created = results.Single(result =>
            result.Status == SetupLiveApplicationStatus.Created);
        SetupLiveEnrollmentResult duplicate = results.Single(result =>
            result.Status == SetupLiveApplicationStatus.Duplicate);
        await Assert.That(created.Capability).IsNotNull();
        await Assert.That(created.Data!.Issuance).IsEqualTo(
            SetupEnrollmentIssuance.Issued);
        await Assert.That(duplicate.Capability).IsNull();
        await Assert.That(duplicate.Data!.Issuance).IsEqualTo(
            SetupEnrollmentIssuance.AlreadyIssued);
        await Assert.That(results.Select(result => result.Data!.EnrollmentId).Distinct())
            .HasSingleItem();

        await using ExploreDbContext verify = fixture.CreateDbContext();
        await Assert.That(await verify.Set<SetupTargetEnrollment>().CountAsync())
            .IsEqualTo(1);
        await Assert.That(await verify.Set<SetupEnrollmentIssuanceClaim>().CountAsync())
            .IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentApplicationSecretWritesUseOnePostgreSqlLeaseAndOneProviderCall()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        var enrollmentRequest = new CreateSetupTargetEnrollmentRequest
        {
            ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
            RequestedScopes = [SetupEnrollmentScope.SecretBindingWrite]
        };
        SetupLiveEnrollmentResult enrollmentResult;
        await using (ExploreDbContext enrollmentContext = fixture.CreateDbContext())
        {
            enrollmentResult = await ApplicationService(enrollmentContext, tenantId)
                .CreateAsync(
                    tenantId,
                    userId,
                    Guid.CreateVersion7(),
                    enrollmentRequest,
                    CancellationToken.None);
            var binding = new SecretBinding
            {
                Id = Guid.CreateVersion7(),
                SettingKey = "setup.signing",
                Scope = SecretScope.Instance,
                SourceType = SecretSourceType.EnvironmentVariable,
                EnvironmentVariableName = "ISLAMU_SETUP_SIGNING",
                CreatedAt = Now
            };
            enrollmentContext.SecretBindings.Add(binding);
            await enrollmentContext.SaveChangesAsync();
        }

        using var writer = new BlockingAtomicSetupSecretBindingWriter();
        var commitment = new FixedSetupCommitmentAuthority();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await using ExploreDbContext firstContext = fixture.CreateDbContext();
            await using ExploreDbContext secondContext = fixture.CreateDbContext();
            await secondContext.Database.OpenConnectionAsync();
            int secondPid = await secondContext.Database
                .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            SetupLiveApplicationService first = ApplicationService(
                firstContext, tenantId, writer, commitment);
            SetupLiveApplicationService second = ApplicationService(
                secondContext, tenantId, writer, commitment);
            Guid operationKey = Guid.CreateVersion7();
            Task<SetupLiveSecretBindingResult> firstWrite = first.WriteSecretBindingAsync(
                tenantId,
                enrollmentResult.Data!.EnrollmentId,
                userId,
                enrollmentResult.Capability!.ToHeaderValue(),
                operationKey,
                "setup.signing",
                secret,
                CancellationToken.None);
            Task firstMilestone = await Task.WhenAny(
                writer.Started,
                firstWrite).WaitAsync(TimeSpan.FromSeconds(10));
            if (firstMilestone == firstWrite)
            {
                SetupLiveSecretBindingResult early = await firstWrite;
                await Assert.That(early.Status).IsEqualTo(SetupLiveApplicationStatus.Success);
                throw new InvalidOperationException("missing-setup-postgresql-provider-barrier");
            }

            Task<SetupLiveSecretBindingResult> secondWrite = second.WriteSecretBindingAsync(
                tenantId,
                enrollmentResult.Data.EnrollmentId,
                userId,
                enrollmentResult.Capability.ToHeaderValue(),
                operationKey,
                "setup.signing",
                secret,
                CancellationToken.None);
            await Assert.That(await WaitForAdvisoryLockWaiterAsync(secondPid, secondWrite))
                .IsTrue();
            writer.Release();

            SetupLiveSecretBindingResult[] results = await Task.WhenAll(
                    firstWrite,
                    secondWrite)
                .WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(results.Select(result => result.Status))
                .IsEquivalentTo(
                [
                    SetupLiveApplicationStatus.Success,
                    SetupLiveApplicationStatus.Duplicate
                ]);
            await Assert.That(results.Select(result => result.Data!.OperationId).Distinct())
                .HasSingleItem();
            await Assert.That(writer.CallCount).IsEqualTo(1);

            await using ExploreDbContext verify = fixture.CreateDbContext();
            SetupSecretBindingOperation operation = await verify
                .Set<SetupSecretBindingOperation>()
                .AsNoTracking()
                .SingleAsync();
            await Assert.That(operation.State)
                .IsEqualTo(Explore.Domain.SetupLive.SetupSecretBindingOperationState.Succeeded);
            await Assert.That(operation.Outcome)
                .IsEqualTo(Explore.Domain.SetupLive.SetupSecretBindingOperationOutcome.Ready);
            await Assert.That(operation.SettledAt).IsNotNull();
        }
        finally
        {
            writer.Release();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task ConcurrentDifferentBindingInputReturnsConflictAfterPostgreSqlLock()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        SetupLiveEnrollmentResult enrollmentResult;
        await using (ExploreDbContext enrollmentContext = fixture.CreateDbContext())
        {
            enrollmentResult = await ApplicationService(enrollmentContext, tenantId)
                .CreateAsync(
                    tenantId,
                    userId,
                    Guid.CreateVersion7(),
                    new CreateSetupTargetEnrollmentRequest
                    {
                        ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
                        RequestedScopes = [SetupEnrollmentScope.SecretBindingWrite]
                    },
                    CancellationToken.None);
            enrollmentContext.SecretBindings.AddRange(
                SetupBinding("setup.signing", "ISLAMU_SETUP_SIGNING"),
                SetupBinding("setup.encryption", "ISLAMU_SETUP_ENCRYPTION"));
            await enrollmentContext.SaveChangesAsync();
        }

        using var writer = new BlockingAtomicSetupSecretBindingWriter();
        var commitment = new FixedSetupCommitmentAuthority();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await using ExploreDbContext firstContext = fixture.CreateDbContext();
            await using ExploreDbContext secondContext = fixture.CreateDbContext();
            await secondContext.Database.OpenConnectionAsync();
            int secondPid = await secondContext.Database
                .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            SetupLiveApplicationService first = ApplicationService(
                firstContext, tenantId, writer, commitment);
            SetupLiveApplicationService second = ApplicationService(
                secondContext, tenantId, writer, commitment);
            Guid operationKey = Guid.CreateVersion7();
            Task<SetupLiveSecretBindingResult> firstWrite = first.WriteSecretBindingAsync(
                tenantId,
                enrollmentResult.Data!.EnrollmentId,
                userId,
                enrollmentResult.Capability!.ToHeaderValue(),
                operationKey,
                "setup.signing",
                secret,
                CancellationToken.None);
            Task firstMilestone = await Task.WhenAny(writer.Started, firstWrite)
                .WaitAsync(TimeSpan.FromSeconds(10));
            if (firstMilestone == firstWrite)
            {
                SetupLiveSecretBindingResult early = await firstWrite;
                await Assert.That(early.Status).IsEqualTo(SetupLiveApplicationStatus.Success);
                throw new InvalidOperationException("missing-setup-postgresql-provider-barrier");
            }

            Task<SetupLiveSecretBindingResult> secondWrite = second.WriteSecretBindingAsync(
                tenantId,
                enrollmentResult.Data.EnrollmentId,
                userId,
                enrollmentResult.Capability.ToHeaderValue(),
                operationKey,
                "setup.encryption",
                secret,
                CancellationToken.None);
            await Assert.That(await WaitForAdvisoryLockWaiterAsync(secondPid, secondWrite))
                .IsTrue();
            writer.Release();
            SetupLiveSecretBindingResult[] results = await Task.WhenAll(
                    firstWrite,
                    secondWrite)
                .WaitAsync(TimeSpan.FromSeconds(30));

            await Assert.That(results.Select(result => result.Status))
                .IsEquivalentTo(
                [
                    SetupLiveApplicationStatus.Success,
                    SetupLiveApplicationStatus.Conflict
                ]);
            await Assert.That(writer.CallCount).IsEqualTo(1);
            await using ExploreDbContext verify = fixture.CreateDbContext();
            SetupSecretBindingOperation operation = await verify
                .Set<SetupSecretBindingOperation>()
                .AsNoTracking()
                .SingleAsync();
            await Assert.That(operation.BindingKey).IsEqualTo("setup.signing");
            await Assert.That(operation.State)
                .IsEqualTo(Explore.Domain.SetupLive.SetupSecretBindingOperationState.Succeeded);
        }
        finally
        {
            writer.Release();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task AcceptedUnknownWriteIsNeverRedispatchedAfterTerminalPersistenceFailure()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        SetupLiveEnrollmentResult enrollment;
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            enrollment = await ApplicationService(setup, tenantId).CreateAsync(
                tenantId,
                userId,
                Guid.CreateVersion7(),
                new CreateSetupTargetEnrollmentRequest
                {
                    ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
                    RequestedScopes = [SetupEnrollmentScope.SecretBindingWrite]
                },
                CancellationToken.None);
            setup.SecretBindings.Add(SetupBinding(
                "setup.signing", "ISLAMU_SETUP_SIGNING"));
            await setup.SaveChangesAsync();
        }

        using var writer = new BlockingAtomicSetupSecretBindingWriter();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        Guid operationKey = Guid.CreateVersion7();
        try
        {
            await using ExploreDbContext firstContext = fixture.CreateDbContext();
            IUnitOfWork failing = new FailBeforeSerializableExecutionUnitOfWork(
                new EfCoreUnitOfWork(firstContext),
                failOnInvocation: 3);
            SetupLiveApplicationService first = ApplicationService(
                firstContext,
                tenantId,
                writer,
                unitOfWork: failing);
            Task<SetupLiveSecretBindingResult> firstWrite = first.WriteSecretBindingAsync(
                tenantId,
                enrollment.Data!.EnrollmentId,
                userId,
                enrollment.Capability!.ToHeaderValue(),
                operationKey,
                "setup.signing",
                secret,
                CancellationToken.None);
            await writer.Started.WaitAsync(TimeSpan.FromSeconds(10));
            writer.Release();
            await Assert.That(async () => await firstWrite)
                .Throws<TimeoutException>();

            await using ExploreDbContext retryContext = fixture.CreateDbContext();
            SetupLiveSecretBindingResult retry = await ApplicationService(
                retryContext,
                tenantId,
                writer).WriteSecretBindingAsync(
                    tenantId,
                    enrollment.Data.EnrollmentId,
                    userId,
                    enrollment.Capability.ToHeaderValue(),
                    operationKey,
                    "setup.signing",
                    secret,
                    CancellationToken.None);

            await Assert.That(retry.Status)
                .IsEqualTo(SetupLiveApplicationStatus.Duplicate);
            await Assert.That(retry.Data!.State)
                .IsEqualTo(ISLAMU.Wire.Contracts.SetupLive.SetupSecretBindingOperationState.Accepted);
            await Assert.That(writer.CallCount).IsEqualTo(1);
        }
        finally
        {
            writer.Release();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task ProviderDispatchLeaseMakesRevokeWaitAndPreservesTerminalWrite()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        SetupLiveEnrollmentResult enrollment;
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            enrollment = await ApplicationService(setup, tenantId).CreateAsync(
                tenantId,
                userId,
                Guid.CreateVersion7(),
                new CreateSetupTargetEnrollmentRequest
                {
                    ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
                    RequestedScopes =
                    [
                        SetupEnrollmentScope.TargetRead,
                        SetupEnrollmentScope.SecretBindingWrite
                    ]
                },
                CancellationToken.None);
            setup.SecretBindings.Add(SetupBinding(
                "setup.signing", "ISLAMU_SETUP_SIGNING"));
            await setup.SaveChangesAsync();
        }

        using var writer = new BlockingAtomicSetupSecretBindingWriter();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await using ExploreDbContext writeContext = fixture.CreateDbContext();
            await using ExploreDbContext revokeContext = fixture.CreateDbContext();
            await revokeContext.Database.OpenConnectionAsync();
            int revokePid = await revokeContext.Database
                .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            SetupLiveApplicationService writeService = ApplicationService(
                writeContext, tenantId, writer);
            SetupLiveApplicationService revokeService = ApplicationService(
                revokeContext, tenantId, writer);
            Task<SetupLiveSecretBindingResult> write = writeService.WriteSecretBindingAsync(
                tenantId,
                enrollment.Data!.EnrollmentId,
                userId,
                enrollment.Capability!.ToHeaderValue(),
                Guid.CreateVersion7(),
                "setup.signing",
                secret,
                CancellationToken.None);
            await writer.Started.WaitAsync(TimeSpan.FromSeconds(10));
            Task<SetupLiveEnrollmentResult> revoke = revokeService.RevokeAsync(
                tenantId,
                enrollment.Data.EnrollmentId,
                userId,
                Guid.CreateVersion7(),
                enrollment.Capability.ToHeaderValue(),
                CancellationToken.None);
            await Assert.That(await WaitForAdvisoryLockWaiterAsync(revokePid, revoke))
                .IsTrue();
            writer.Release();

            await Assert.That((await write).Status)
                .IsEqualTo(SetupLiveApplicationStatus.Success);
            await Assert.That((await revoke).Status)
                .IsEqualTo(SetupLiveApplicationStatus.Success);
            await Assert.That(writer.CallCount).IsEqualTo(1);
        }
        finally
        {
            writer.Release();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task RevokeLeaseMakesWriteWaitAndPreventsProviderDispatch()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid userId) = await SeedApplicationActorAsync();
        SetupLiveEnrollmentResult enrollment;
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            enrollment = await ApplicationService(setup, tenantId).CreateAsync(
                tenantId,
                userId,
                Guid.CreateVersion7(),
                new CreateSetupTargetEnrollmentRequest
                {
                    ClientChallenge = SetupClientChallenge.FromBytes(new byte[32]),
                    RequestedScopes =
                    [
                        SetupEnrollmentScope.TargetRead,
                        SetupEnrollmentScope.SecretBindingWrite
                    ]
                },
                CancellationToken.None);
            setup.SecretBindings.Add(SetupBinding(
                "setup.signing", "ISLAMU_SETUP_SIGNING"));
            await setup.SaveChangesAsync();
        }

        using var writer = new BlockingAtomicSetupSecretBindingWriter();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await using ExploreDbContext revokeContext = fixture.CreateDbContext();
            await using ExploreDbContext writeContext = fixture.CreateDbContext();
            await writeContext.Database.OpenConnectionAsync();
            int writePid = await writeContext.Database
                .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            var blocker = new BlockingAfterAcquireCoordinator(
                new RelationalSetupSecretBindingOperationCoordinator(revokeContext));
            SetupLiveApplicationService revokeService = ApplicationService(
                revokeContext, tenantId, writer, coordinator: blocker);
            SetupLiveApplicationService writeService = ApplicationService(
                writeContext, tenantId, writer);
            Task<SetupLiveEnrollmentResult> revoke = revokeService.RevokeAsync(
                tenantId,
                enrollment.Data!.EnrollmentId,
                userId,
                Guid.CreateVersion7(),
                enrollment.Capability!.ToHeaderValue(),
                CancellationToken.None);
            await blocker.Started.WaitAsync(TimeSpan.FromSeconds(10));
            Task<SetupLiveSecretBindingResult> write = writeService.WriteSecretBindingAsync(
                tenantId,
                enrollment.Data.EnrollmentId,
                userId,
                enrollment.Capability.ToHeaderValue(),
                Guid.CreateVersion7(),
                "setup.signing",
                secret,
                CancellationToken.None);
            await Assert.That(await WaitForAdvisoryLockWaiterAsync(writePid, write))
                .IsTrue();
            blocker.Release();

            await Assert.That((await revoke).Status)
                .IsEqualTo(SetupLiveApplicationStatus.Success);
            await Assert.That((await write).Status)
                .IsEqualTo(SetupLiveApplicationStatus.Unavailable);
            await Assert.That(writer.CallCount).IsEqualTo(0);
        }
        finally
        {
            writer.Release();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task TenantAndActorLineageAreRejectedByNamedForeignKeys()
    {
        await fixture.ResetAsync();
        Guid owningTenant = Guid.CreateVersion7();
        SetupTargetEnrollment enrollment = Enrollment(owningTenant);
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.Tenants.Add(Tenant(owningTenant, "lineage-owner"));
            seed.Set<SetupTargetEnrollment>().Add(enrollment);
            await seed.SaveChangesAsync();
        }

        Guid foreignTenant = Guid.CreateVersion7();
        Guid foreignActor = Guid.CreateVersion7();
        await using (ExploreDbContext foreignTenantSeed = fixture.CreateDbContext())
        {
            foreignTenantSeed.Tenants.Add(Tenant(foreignTenant, "lineage-foreign"));
            await foreignTenantSeed.SaveChangesAsync();
        }
        await AssertForeignKeyRejectedAsync(
            context => context.Set<SetupEnrollmentIssuanceClaim>().Add(
                Claim(enrollment, Guid.CreateVersion7(), foreignTenant)),
            ClaimEnrollmentConstraint);
        await AssertForeignKeyRejectedAsync(
            context => context.Set<SetupSecretBindingOperation>().Add(
                Operation(enrollment, Guid.CreateVersion7(), foreignTenant)),
            OperationEnrollmentConstraint);
        await AssertForeignKeyRejectedAsync(
            context => context.Set<SetupEnrollmentIssuanceClaim>().Add(
                Claim(enrollment, Guid.CreateVersion7(), actorId: foreignActor)),
            ClaimEnrollmentConstraint);
        await AssertForeignKeyRejectedAsync(
            context => context.Set<SetupSecretBindingOperation>().Add(
                Operation(enrollment, Guid.CreateVersion7(), actorId: foreignActor)),
            OperationEnrollmentConstraint);

        await using ExploreDbContext verify = fixture.CreateDbContext();
        await Assert.That(await verify.Set<SetupEnrollmentIssuanceClaim>().CountAsync())
            .IsEqualTo(0);
        await Assert.That(await verify.Set<SetupSecretBindingOperation>().CountAsync())
            .IsEqualTo(0);
    }

    [Test]
    public async Task DatabaseRejectsNonpositiveSecurityVersions()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        SetupTargetEnrollment enrollment = Enrollment(tenantId);
        SetupEnrollmentIssuanceClaim claim = Claim(
            enrollment,
            Guid.CreateVersion7());
        SetupSecretBindingOperation operation = Operation(
            enrollment,
            Guid.CreateVersion7());
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.Tenants.Add(Tenant(tenantId, "versions"));
            seed.Set<SetupTargetEnrollment>().Add(enrollment);
            seed.Set<SetupEnrollmentIssuanceClaim>().Add(claim);
            seed.Set<SetupSecretBindingOperation>().Add(operation);
            await seed.SaveChangesAsync();
        }

        foreach (long invalid in new long[] { 0, -1 })
        {
            await AssertCheckRejectedAsync(
                $"UPDATE setup_target_enrollments SET generation = {invalid} WHERE id = {enrollment.Id}",
                "ck_setup_target_enrollments_generation");
            await AssertCheckRejectedAsync(
                $"UPDATE setup_enrollment_issuance_claims SET enrollment_generation = {invalid} WHERE id = {claim.Id}",
                "ck_setup_enrollment_claims_generation");
            await AssertCheckRejectedAsync(
                $"UPDATE setup_secret_binding_operations SET enrollment_generation = {invalid} WHERE id = {operation.Id}",
                "ck_setup_secret_operations_versions");
            await AssertCheckRejectedAsync(
                $"UPDATE setup_secret_binding_operations SET commitment_key_version = {invalid} WHERE id = {operation.Id}",
                "ck_setup_secret_operations_versions");
        }
    }

    [Test]
    public async Task RepositoryHonorsOptimisticConcurrencyAndTransactionRollback()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        SetupTargetEnrollment enrollment = Enrollment(tenantId);
        SetupTargetEnrollment otherEnrollment = Enrollment(otherTenantId);
        Guid persistedOperation = Guid.CreateVersion7();
        Guid otherPersistedOperation = Guid.CreateVersion7();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(
                Tenant(tenantId, "repository-a"),
                Tenant(otherTenantId, "repository-b"));
            var repository = new SetupLiveRepository(seed);
            await repository.AddAsync(enrollment, CancellationToken.None);
            await repository.AddAsync(otherEnrollment, CancellationToken.None);
            await repository.AddAsync(
                Claim(enrollment, persistedOperation),
                CancellationToken.None);
            await repository.AddAsync(
                Claim(otherEnrollment, otherPersistedOperation),
                CancellationToken.None);
            await repository.AddAsync(
                Operation(enrollment, persistedOperation),
                CancellationToken.None);
            await repository.AddAsync(
                Operation(otherEnrollment, otherPersistedOperation),
                CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using (ExploreDbContext systemContext = fixture.CreateDbContext())
        {
            var systemRepository = new SetupLiveRepository(systemContext);
            SetupTargetEnrollment? foundEnrollment =
                await systemRepository.FindEnrollmentAsync(
                tenantId,
                enrollment.Id,
                CancellationToken.None);
            SetupEnrollmentIssuanceClaim? foundClaim =
                await systemRepository.FindIssuanceClaimAsync(
                tenantId,
                persistedOperation,
                CancellationToken.None);
            SetupSecretBindingOperation? foundOperation =
                await systemRepository.FindOperationAsync(
                tenantId,
                persistedOperation,
                CancellationToken.None);
            await Assert.That(foundEnrollment?.TenantId).IsEqualTo(tenantId);
            await Assert.That(foundClaim?.TenantId).IsEqualTo(tenantId);
            await Assert.That(foundOperation?.TenantId).IsEqualTo(tenantId);
            await Assert.That((await systemRepository.FindIssuanceClaimAsync(
                otherTenantId,
                otherPersistedOperation,
                CancellationToken.None))?.TenantId).IsEqualTo(otherTenantId);
            await Assert.That((await systemRepository.FindOperationAsync(
                otherTenantId,
                otherPersistedOperation,
                CancellationToken.None))?.TenantId).IsEqualTo(otherTenantId);
            await Assert.That(await systemRepository.FindEnrollmentAsync(
                otherTenantId,
                enrollment.Id,
                CancellationToken.None)).IsNull();
            await Assert.That(await systemRepository.FindIssuanceClaimAsync(
                otherTenantId,
                persistedOperation,
                CancellationToken.None)).IsNull();
            await Assert.That(await systemRepository.FindOperationAsync(
                tenantId,
                otherPersistedOperation,
                CancellationToken.None)).IsNull();
        }

        await using ExploreDbContext firstContext =
            fixture.CreateTenantFilteredDbContext(new TenantContext(tenantId));
        await using ExploreDbContext staleContext =
            fixture.CreateTenantFilteredDbContext(new TenantContext(tenantId));
        var firstRepository = new SetupLiveRepository(firstContext);
        var staleRepository = new SetupLiveRepository(staleContext);
        SetupTargetEnrollment first = (await firstRepository.FindEnrollmentAsync(
            tenantId,
            enrollment.Id,
            CancellationToken.None))!;
        SetupTargetEnrollment stale = (await staleRepository.FindEnrollmentAsync(
            tenantId,
            enrollment.Id,
            CancellationToken.None))!;
        first.RotateCapability(Digest('d'), Now.AddMinutes(20), Now.AddMinutes(1));
        await firstRepository.SaveChangesAsync(CancellationToken.None);
        stale.RotateCapability(Digest('e'), Now.AddMinutes(30), Now.AddMinutes(1));

        await Assert.That(() => staleRepository.SaveChangesAsync(
                CancellationToken.None))
            .Throws<DbUpdateConcurrencyException>();

        Guid rollbackOperation = Guid.CreateVersion7();
        await using ExploreDbContext rollbackContext =
            fixture.CreateTenantFilteredDbContext(new TenantContext(tenantId));
        var rollbackRepository = new SetupLiveRepository(rollbackContext);
        var unitOfWork = new EfCoreUnitOfWork(rollbackContext);
        await Assert.That(() => unitOfWork.ExecuteInTransactionAsync(
                async cancellationToken =>
                {
                    await rollbackRepository.AddAsync(
                        Claim(enrollment, rollbackOperation),
                        cancellationToken);
                    await rollbackRepository.AddAsync(
                        Operation(enrollment, rollbackOperation),
                        cancellationToken);
                    await rollbackRepository.SaveChangesAsync(cancellationToken);
                    throw new SetupRollbackSentinelException();
                }))
            .Throws<SetupRollbackSentinelException>();

        await using ExploreDbContext verify = fixture.CreateDbContext();
        await Assert.That(await verify.Set<SetupEnrollmentIssuanceClaim>().CountAsync(
            claim => claim.OperationKey == rollbackOperation)).IsEqualTo(0);
        await Assert.That(await verify.Set<SetupSecretBindingOperation>().CountAsync(
            operation => operation.OperationKey == rollbackOperation)).IsEqualTo(0);
    }

    [Test]
    public async Task CoordinatorSerializesOnlyTheExactEnrollmentGeneration()
    {
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        await using ExploreDbContext otherContext = fixture.CreateDbContext();
        await secondContext.Database.OpenConnectionAsync();
        int secondBackend = await secondContext.Database
            .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync();
        var firstCoordinator =
            new RelationalSetupSecretBindingOperationCoordinator(firstContext);
        var secondCoordinator =
            new RelationalSetupSecretBindingOperationCoordinator(secondContext);
        var otherCoordinator =
            new RelationalSetupSecretBindingOperationCoordinator(otherContext);
        Guid tenantId = Guid.CreateVersion7();
        Guid enrollmentId = Guid.CreateVersion7();
        var request = new SetupSecretBindingCoordinationRequest(
            tenantId,
            enrollmentId,
            7);
        IAsyncDisposable firstLease = await firstCoordinator.AcquireAsync(
            request,
            CancellationToken.None);
        Task<IAsyncDisposable> competing;
        try
        {
            competing = secondCoordinator.AcquireAsync(
                request,
                CancellationToken.None);
            await Assert.That(await WaitForAdvisoryLockWaiterAsync(
                secondBackend,
                competing)).IsTrue();
            await using IAsyncDisposable otherTenant = await otherCoordinator.AcquireAsync(
                new SetupSecretBindingCoordinationRequest(
                    Guid.CreateVersion7(),
                    enrollmentId,
                    7),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await using IAsyncDisposable otherEnrollment = await otherCoordinator.AcquireAsync(
                new SetupSecretBindingCoordinationRequest(
                    tenantId,
                    Guid.CreateVersion7(),
                    7),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await using IAsyncDisposable otherGeneration = await otherCoordinator.AcquireAsync(
                new SetupSecretBindingCoordinationRequest(
                    tenantId,
                    enrollmentId,
                    8),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await firstLease.DisposeAsync();
        }
        await using IAsyncDisposable secondLease = await competing.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    private async Task AssertUniqueRaceAsync(
        Action<ExploreDbContext> addWinner,
        Action<ExploreDbContext> addContender,
        string expectedConstraint)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using ExploreDbContext winner = fixture.CreateDbContext();
        await using ExploreDbContext contender = fixture.CreateDbContext();
        await contender.Database.OpenConnectionAsync(timeout.Token);
        int contenderBackend = await contender.Database
            .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(timeout.Token);
        await winner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await winner.Database
                .BeginTransactionAsync(timeout.Token);
            addWinner(winner);
            await winner.SaveChangesAsync(timeout.Token);
            addContender(contender);
            Task contenderSave = contender.SaveChangesAsync(timeout.Token);
            await Assert.That(await WaitForLockWaiterAsync(
                contenderBackend,
                contenderSave,
                timeout.Token)).IsTrue();
            await transaction.CommitAsync(timeout.Token);

            Exception failure = (await Assert.That(async () => await contenderSave)
                .Throws<Exception>())!;
            PostgresException postgres = FindPostgresException(failure);
            await Assert.That(postgres.SqlState).IsEqualTo(
                PostgresErrorCodes.UniqueViolation);
            await Assert.That(postgres.ConstraintName).IsEqualTo(expectedConstraint);
        });
    }

    private async Task AssertForeignKeyRejectedAsync(
        Action<ExploreDbContext> addAttack,
        string expectedConstraint)
    {
        await using ExploreDbContext attack = fixture.CreateDbContext();
        addAttack(attack);
        Exception failure = (await Assert.That(() => attack.SaveChangesAsync())
            .Throws<Exception>())!;
        PostgresException postgres = FindPostgresException(failure);
        await Assert.That(postgres.SqlState).IsEqualTo(
            PostgresErrorCodes.ForeignKeyViolation);
        await Assert.That(postgres.ConstraintName).IsEqualTo(expectedConstraint);
        attack.ChangeTracker.Clear();
    }

    private async Task AssertCheckRejectedAsync(
        FormattableString command,
        string expectedConstraint)
    {
        await using ExploreDbContext attack = fixture.CreateDbContext();
        Exception failure = (await Assert.That(() =>
                attack.Database.ExecuteSqlInterpolatedAsync(command))
            .Throws<Exception>())!;
        PostgresException postgres = FindPostgresException(failure);
        await Assert.That(postgres.SqlState).IsEqualTo(
            PostgresErrorCodes.CheckViolation);
        await Assert.That(postgres.ConstraintName).IsEqualTo(expectedConstraint);
    }

    private async Task<bool> WaitForAdvisoryLockWaiterAsync(
        int backendPid,
        Task competing)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await WaitForLockWaiterAsync(
            backendPid,
            competing,
            timeout.Token,
            "advisory");
    }

    private async Task<bool> WaitForLockWaiterAsync(
        int backendPid,
        Task competing,
        CancellationToken cancellationToken,
        string? lockType = null)
    {
        await using ExploreDbContext observer = fixture.CreateDbContext();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (competing.IsCompleted)
                return false;
            int waiting = lockType is null
                ? await observer.Database.SqlQuery<int>($$"""
                    SELECT COUNT(*)::integer AS "Value"
                    FROM pg_locks
                    WHERE pid = {{backendPid}}
                      AND NOT granted
                    """).SingleAsync(cancellationToken)
                : await observer.Database.SqlQuery<int>($$"""
                    SELECT COUNT(*)::integer AS "Value"
                    FROM pg_locks
                    WHERE pid = {{backendPid}}
                      AND NOT granted
                      AND locktype = {{lockType}}
                    """).SingleAsync(cancellationToken);
            if (waiting > 0)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        return false;
    }

    private static PostgresException FindPostgresException(Exception exception) =>
        exception is PostgresException postgres
            ? postgres
            : exception.InnerException is not null
                ? FindPostgresException(exception.InnerException)
                : throw new InvalidOperationException(
                    "Expected a PostgreSQL constraint failure.",
                    exception);

    private static SetupTargetEnrollment Enrollment(Guid tenantId) =>
        SetupTargetEnrollment.Create(
            Guid.CreateVersion7(),
            tenantId,
            Guid.CreateVersion7(),
            Digest('a'),
            Digest('b'),
            Digest('c'),
            Now,
            Now.AddMinutes(10));

    private static SecretBinding SetupBinding(string settingKey, string variableName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SettingKey = settingKey,
            Scope = SecretScope.Instance,
            SourceType = SecretSourceType.EnvironmentVariable,
            EnvironmentVariableName = variableName,
            CreatedAt = Now
        };

    private SetupLiveApplicationService ApplicationService(
        ExploreDbContext context,
        Guid tenantId,
        ISetupSecretBindingWriter? writer = null,
        ISetupSecretBindingCommitmentAuthority? commitmentAuthority = null,
        IUnitOfWork? unitOfWork = null,
        ISetupSecretBindingOperationCoordinator? coordinator = null) => new(
        new SetupLiveRepository(context),
        new SecretBindingRepository(context),
        unitOfWork ?? new EfCoreUnitOfWork(context),
        new ActorRepository(context),
        new AllowAuthorizationProvider(),
        new TenantContext(tenantId),
        writer ?? new NoopSetupSecretBindingWriter(),
        writer as ISetupSecretBindingReadinessReader
            ?? new NoopSetupSecretBindingWriter(),
        commitmentAuthority ?? new FixedSetupCommitmentAuthority(),
        coordinator ?? new RelationalSetupSecretBindingOperationCoordinator(context),
        new ImmediateSetupCommitBarrier(),
        TimeProvider.System,
        NullLogger<SetupLiveApplicationService>.Instance);

    private async Task<(Guid TenantId, Guid UserId)> SeedApplicationActorAsync()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var tenant = Tenant(tenantId, "application-race");
        var user = new User
        {
            Id = userId,
            Pii = new UserPii
            {
                Email = $"setup-{userId:N}@example.test",
                FirstName = "Setup",
                LastName = "Race"
            },
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = Now
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Id = actorId,
            Pii = new ActorPii { DisplayName = "Setup race actor" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = userId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = Now
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            UserId = userId,
            User = user,
            ActorId = actorId,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = Now,
            CreatedAt = Now
        });
        await context.SaveChangesAsync();
        return (tenantId, userId);
    }

    private static SetupEnrollmentIssuanceClaim Claim(
        SetupTargetEnrollment enrollment,
        Guid operationKey,
        Guid? tenantId = null,
        Guid? actorId = null) =>
        SetupEnrollmentIssuanceClaim.Create(
            Guid.CreateVersion7(),
            tenantId ?? enrollment.TenantId,
            actorId ?? enrollment.ActorId,
            operationKey,
            enrollment.Id,
            enrollment.Generation,
            Digest('d'),
            Now.AddSeconds(1));

    private static SetupSecretBindingOperation Operation(
        SetupTargetEnrollment enrollment,
        Guid operationKey,
        Guid? tenantId = null,
        Guid? actorId = null) =>
        SetupSecretBindingOperation.CreateAccepted(
            Guid.CreateVersion7(),
            tenantId ?? enrollment.TenantId,
            actorId ?? enrollment.ActorId,
            enrollment.Id,
            enrollment.Generation,
            operationKey,
            "setup.signing",
            Digest('e'),
            7,
            Digest('f'),
            Now.AddSeconds(1));

    private static Tenant Tenant(Guid tenantId, string slug) => new()
    {
        Id = tenantId,
        FullName = $"Setup live {slug}",
        Slug = $"{slug}-{tenantId:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static string Digest(char value) => new(value, 64);

    private sealed class SetupRollbackSentinelException : Exception;

    private sealed class NoopSetupSecretBindingWriter :
        ISetupSecretBindingWriter,
        ISetupSecretBindingReadinessReader
    {
        public Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
            Guid bindingId,
            string bindingKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(SetupSecretBindingWriteOutcome.Unavailable);

        public Task<SetupSecretBindingWriteOutcome> WriteAsync(
            SetupSecretBindingWriteRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(SetupSecretBindingWriteOutcome.Unavailable);
    }

    private sealed class FixedSetupCommitmentAuthority :
        ISetupSecretBindingCommitmentAuthority
    {
        public Task<SetupSecretBindingCommitment> CommitAsync(
            SetupSecretBindingCommitmentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SetupSecretBindingCommitment(37, Digest('f')));
    }

    private sealed class ImmediateSetupCommitBarrier : ISetupSecretBindingCommitBarrier
    {
        public Task WaitBeforeProviderDispatchAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingAtomicSetupSecretBindingWriter :
        ISetupSecretBindingWriter,
        ISetupSecretBindingReadinessReader,
        IDisposable
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public Task Started => _started.Task;

        public Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
            Guid bindingId,
            string bindingKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(SetupSecretBindingWriteOutcome.Ready);

        public Task<SetupSecretBindingWriteOutcome> WriteAsync(
            SetupSecretBindingWriteRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            _started.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10), cancellationToken))
                throw new TimeoutException("setup-postgresql-writer-release-timeout");
            return Task.FromResult(SetupSecretBindingWriteOutcome.Ready);
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            Release();
            _release.Dispose();
        }
    }

    private sealed class FailBeforeSerializableExecutionUnitOfWork(
        IUnitOfWork inner,
        int failOnInvocation) : IUnitOfWork
    {
        private int _invocations;

        public Task<T> ExecuteReadCommittedAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => inner.ExecuteReadCommittedAsync(operation, ct);

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) =>
            inner.ExecuteInTransactionAsync(operation, ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            inner.ExecuteInTransactionAsync(operation, ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _invocations) == failOnInvocation)
                throw new TimeoutException("setup-terminal-persistence-failure");
            return inner.ExecuteSerializableAsync(operation, ct);
        }
    }

    private sealed class BlockingAfterAcquireCoordinator(
        ISetupSecretBindingOperationCoordinator inner) :
        ISetupSecretBindingOperationCoordinator
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public void Release() => _release.TrySetResult();

        public async Task<IAsyncDisposable> AcquireAsync(
            SetupSecretBindingCoordinationRequest request,
            CancellationToken cancellationToken)
        {
            IAsyncDisposable lease = await inner.AcquireAsync(
                request,
                cancellationToken);
            try
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                return lease;
            }
            catch
            {
                await lease.DisposeAsync();
                throw;
            }
        }
    }

    private sealed class AllowAuthorizationProvider : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizationDecision.Allow(
                AuthorizationProviderMetadata.Local));

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizationDecision>>(
                requests.Select(_ => AuthorizationDecision.Allow(
                    AuthorizationProviderMetadata.Local)).ToArray());
    }

    private sealed class ClaimReadRendezvousInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "setup_enrollment_issuance_claims",
                    StringComparison.Ordinal)
                && Interlocked.Increment(ref _arrivals) <= 2)
            {
                if (Volatile.Read(ref _arrivals) == 2)
                    _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(
                    TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;
}
