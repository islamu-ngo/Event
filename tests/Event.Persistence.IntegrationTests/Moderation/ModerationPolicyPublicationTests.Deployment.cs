using System.Security.Claims;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    public async Task DeploymentAdoption_RequalifiesExactCandidateCoverageOnOriginalBorrow(int change)
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var clock = new DeploymentClock();
        await using var context = database.Context();
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var runtimes = database.Inventory.Consumers.Keys.Select(id =>
            new DeploymentRuntime(id, database.Inventory.Fingerprint)).ToArray();
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine,
            database.Inventory, null, clock, runtimes);
        var candidate = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(candidate.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(candidate.Candidate!, database.Inventory)!;
        var requiredIds = policy.Consumers.SelectMany(row => row.Consumers).Select(row => row.Id).Distinct().ToArray();
        var writer = new ModerationQualificationRepository(context, admin, machine, publications,
            new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
            clock, database.Privacy.Authority(context, clock), runtimes, deploymentInventory: database.Inventory);
        long epoch = 2;
        var installations = new List<InstallModerationConsumerSource>();
        foreach (var id in requiredIds)
        {
            var runtime = runtimes.Single(row => row.ConsumerId == id);
            var installed = new InstallModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch++,
                InstallationId = Guid.CreateVersion7(),
                ConsumerId = id,
                Mode = ModerationNativeMode.Embedded,
                ExpectedRuntimeGeneration = runtime.Generation,
                ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
                ValidUntil = Now.AddMinutes(10)
            };
            await Assert.That((await writer.ExecuteAsync(installed)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            installations.Add(installed);
        }
        var record = new RecordModerationDeploymentSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch++,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(5)
        };
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopt = new AdoptModerationDeploymentSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = epoch,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await writer.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        var approverId = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        var approver = new User
        {
            Id = approverId,
            Pii = new UserPii
            {
                UserId = approverId,
                Email = $"{approverId:N}@example.invalid",
                FirstName = "Deployment",
                LastName = "Adopter"
            }
        };
        context.Users.Add(approver);
        context.PlatformUserRoles.Add(new PlatformUserRole
        { Id = Guid.CreateVersion7(), UserId = approverId, User = approver, RoleId = role.Id, Role = role });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var (secondAdmin, secondMachine) = DeploymentIdentity(context, approverId);
        var independent = new ModerationQualificationRepository(context, secondAdmin, secondMachine, publications,
            new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
            clock, database.Privacy.Authority(context, clock), runtimes, deploymentInventory: database.Inventory);
        await Assert.That((await independent.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await independent.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        epoch++;
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        var retained = await context.Set<ModerationDeploymentSource>().AsNoTracking().Include(row => row.Consumers).SingleAsync();
        await Assert.That(retained.DefinitionId).IsEqualTo(candidate.Candidate!.DefinitionId);
        await Assert.That(retained.Consumers.Count).IsEqualTo(policy.Consumers.Sum(row => row.Consumers.Length));

        if (change == 1) runtimes.Single(row => row.ConsumerId == requiredIds[0]).Generation = new string('c', 64);
        if (change == 2) runtimes.Single(row => row.ConsumerId == requiredIds[0]).Product = new string('d', 64);
        if (change is 3 or 4 or 5 or 13)
        {
            if (change == 3)
                context.ModerationQualificationMutationReceipts.Remove(await context.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.Id == record.OperationId));
            else if (change == 4)
                context.ModerationQualificationMutationReceipts.Remove(await context.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.Id == installations[0].OperationId));
            else if (change == 5)
                context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == approverId));
            else
                context.ModerationQualificationMutationReceipts.Remove(await context.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.Id == adopt.OperationId));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change == 6)
        {
            await Assert.That((await writer.ExecuteAsync(record with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = epoch
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == 7) clock.UtcNow = Now.AddMinutes(4);
        if (change == 10)
        {
            clock.UtcNow = Now.AddMinutes(10);
            var withdrawal = new WithdrawModerationDeploymentAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        using var evidence = publications.BorrowConsumerEvidence(authority!, policy);
        if (change == 8) evidence.Dispose();
        if (change == 11) context.EnableTenantFilterBypass("Deployment isolation adversarial witness.");
        if (change == 12) await transaction.RollbackAsync();
        await using var replacementContext = database.Context();
        var ownerContext = change == 15 ? replacementContext : context;
        var definitions = new ModerationPolicyDefinitionSourceOwner(ownerContext, admin, machine, database.Inventory, clock);
        var owner = new ModerationDeploymentSourceOwner(ownerContext, definitions, database.Inventory, runtimes, clock);
        var current = await owner.AcquireSourceAsync(evidence, transaction.TransactionId, record.ProfileId,
            record.Mode, clock.UtcNow, default);
        var adopted = await owner.ReadAdoptionAsync(evidence,
            change == 9 ? Guid.CreateVersion7() : transaction.TransactionId, record.ProfileId,
            change == 14 ? ModerationNativeMode.RemoteStrict : record.Mode, clock.UtcNow, default);
        if (change == 0)
        {
            await Assert.That(current!.Source.Id).IsEqualTo(record.SourceId);
            await Assert.That(adopted!.Adoption.Id).IsEqualTo(adopt.AdoptionId);
            await Assert.That(adopted.ValidUntil).IsEqualTo(adopt.ValidUntil);
        }
        else await Assert.That(adopted).IsNull();
    }

    [Test]
    public async Task DeploymentPreparation_CannotPromoteDefinitionWithoutComposedRuntimeOwners()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var publication = database.Writer(context);
        await Assert.That((await publication.PrepareAsync(request, Now.AddMinutes(10))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var writer = new ModerationQualificationRepository(context, admin, machine, publication,
            new ModerationNativeActivationQualification(context, database.Inventory, null, null, new FixedClock()),
            new FixedClock(), database.Privacy.Authority(context, new FixedClock()), deploymentInventory: database.Inventory);
        var result = await writer.ExecuteAsync(new RecordModerationDeploymentSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(5)
        });
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationDeploymentSource>().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Set<ModerationDeploymentAdoption>().CountAsync()).IsEqualTo(0);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task DeploymentOwner_RejectsInterfaceOnlySessionBeforeCallingAnyObservation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        using var evidence = new InterfaceOnlyDeploymentSession();
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, new FixedClock());
        var owner = new ModerationDeploymentSourceOwner(context, definitions, database.Inventory, [], new FixedClock());
        await Assert.That(await owner.AcquireSourceAsync(evidence, evidence.TransactionId,
            Guid.CreateVersion7(), ModerationNativeMode.Embedded, Now, default)).IsNull();
        await Assert.That(await owner.ReadAdoptionAsync(evidence, evidence.TransactionId,
            Guid.CreateVersion7(), ModerationNativeMode.Embedded, Now, default)).IsNull();
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) DeploymentIdentity(ExploreDbContext context, Guid userId)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "deployment-witness"))
            }
        };
        return (new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context),
            new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance), new MachinePrincipalAccessor(http));
    }

    private sealed class DeploymentClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    // Controlled host artifact boundary for invariant tests only, never registered as a production owner.
    private sealed class DeploymentRuntime(string consumerId, string inventory) : IModerationConsumerRuntimeSource
    {
        public string ConsumerId => consumerId;
        public ModerationNativeMode Mode => ModerationNativeMode.Embedded;
        public string Generation { get; set; } = new('a', 64);
        public string Product { get; set; } = new('b', 64);
        public ModerationConsumerRuntimeObservation ReadCurrent() => new(ConsumerId, Mode, Generation, inventory, Product);
    }

    private sealed class InterfaceOnlyDeploymentSession : IModerationConsumerEvidenceSession
    {
        public Guid TransactionId { get; } = Guid.CreateVersion7();
        public void Dispose() { }
        public Task<ModerationMeasurementDatabaseObservation?> AcquirePrimaryDatabaseAsync(
            DateTimeOffset observedAt, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ModerationCapabilitySourceObservation?> AcquireInstallationAsync(ModerationActivationBinding binding,
            string consumerId, DateTimeOffset observedAt, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ModerationMeasurementReceiptObservation?> AcquireMeasurementAsync(ModerationActivationBinding binding,
            string consumerId, DateTimeOffset observedAt, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
