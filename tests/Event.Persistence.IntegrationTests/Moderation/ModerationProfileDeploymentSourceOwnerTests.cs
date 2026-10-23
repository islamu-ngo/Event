using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Real SQLite source writers and the actual lifecycle configuration reader qualify the join.
/// The host runtime boundary is controlled; no configuration, deployment or adoption is mocked.
/// These authored scenarios do not establish measured conformance or activation readiness.
/// </summary>
public sealed class ModerationProfileDeploymentSourceOwnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public enum Change
    {
        None, ConfigurationBoundsDeployment, MissingConfiguration, MissingDeployment, MissingDeploymentAdoption,
        WithdrawnDeploymentAdoption, RevokedConfiguration, MissingSelectionReceipt, MissingProfileReceipt,
        MissingDeploymentReceipt, MissingDeploymentAdoptionReceipt, MissingInstallationReceipt,
        ChangedArtifact, ChangedRuntime, ArtifactDriftsDuringConfigurationRead, ConfigurationDriftsDuringRead,
        AdoptionDriftsDuringRead, AuthorityDriftsDuringRead, ExpiryDuringRead, ClosedBorrow,
        WrongTransaction, WrongHost, OtherContext, DetachedDecision, OtherCompiledPolicy, PublicationBorrow,
        ReplacedTransaction, ExpiredConfiguration, ExpiredDeploymentAdoption, TenantBypass
    }

    [Test]
    [Arguments(Change.None)]
    [Arguments(Change.ConfigurationBoundsDeployment)]
    [Arguments(Change.MissingConfiguration)]
    [Arguments(Change.MissingDeployment)]
    [Arguments(Change.MissingDeploymentAdoption)]
    [Arguments(Change.WithdrawnDeploymentAdoption)]
    [Arguments(Change.RevokedConfiguration)]
    [Arguments(Change.MissingSelectionReceipt)]
    [Arguments(Change.MissingProfileReceipt)]
    [Arguments(Change.MissingDeploymentReceipt)]
    [Arguments(Change.MissingDeploymentAdoptionReceipt)]
    [Arguments(Change.MissingInstallationReceipt)]
    [Arguments(Change.ChangedArtifact)]
    [Arguments(Change.ChangedRuntime)]
    [Arguments(Change.ArtifactDriftsDuringConfigurationRead)]
    [Arguments(Change.ConfigurationDriftsDuringRead)]
    [Arguments(Change.AdoptionDriftsDuringRead)]
    [Arguments(Change.AuthorityDriftsDuringRead)]
    [Arguments(Change.ExpiryDuringRead)]
    [Arguments(Change.ClosedBorrow)]
    [Arguments(Change.WrongTransaction)]
    [Arguments(Change.WrongHost)]
    [Arguments(Change.OtherContext)]
    [Arguments(Change.DetachedDecision)]
    [Arguments(Change.OtherCompiledPolicy)]
    [Arguments(Change.PublicationBorrow)]
    [Arguments(Change.ReplacedTransaction)]
    [Arguments(Change.ExpiredConfiguration)]
    [Arguments(Change.ExpiredDeploymentAdoption)]
    [Arguments(Change.TenantBypass)]
    public Task NativeJoinPreservesOriginalConfigurationAndRejectsIndependentSourceDrift(Change change) =>
        NativeJoinAsync(change, false);

    [Test]
    [Arguments(Change.None)]
    [Arguments(Change.ConfigurationBoundsDeployment)]
    [Arguments(Change.MissingConfiguration)]
    [Arguments(Change.MissingDeploymentAdoption)]
    [Arguments(Change.ClosedBorrow)]
    [Arguments(Change.OtherContext)]
    [Arguments(Change.PublicationBorrow)]
    [Arguments(Change.ReplacedTransaction)]
    [Arguments(Change.TenantBypass)]
    public Task NativeCompositionPreservesProfileFencesWithoutInventingConsumerConformance(Change change) =>
        NativeJoinAsync(change, true);

    private async Task NativeJoinAsync(Change change, bool composed)
    {
        var (database, target) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var databaseOwner = database;
        var profileAdoptionId = Guid.CreateVersion7();
        long epoch = 5;
        if (change != Change.MissingConfiguration)
        {
            await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, new AdoptModerationProfileSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch++,
                AdoptionId = profileAdoptionId,
                SelectionId = target.SelectionId,
                Mode = ModerationNativeMode.Embedded,
                CaseId = target.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = Now.AddMinutes(5)
            })).IsSuccess).IsTrue();
        }
        ModerationPolicyPublication publication;
        ModerationDecision original;
        ModerationContributionSelection selection;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            publication = await reader.ModerationPolicyPublications.AsNoTracking().SingleAsync();
            original = await reader.ModerationDecisions.AsNoTracking().SingleAsync();
            selection = await reader.ModerationContributionSelections.AsNoTracking().SingleAsync();
        }
        var compiled = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        var runtimes = compiled.Consumers.SelectMany(row => row.Consumers).Select(row => row.Id).Distinct()
            .Select(id => new HostRuntime(id, database.Inventory.Fingerprint)).ToArray();
        database.ConsumerRuntimeSources = runtimes;
        foreach (var runtime in runtimes)
        {
            var installation = new InstallModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch++,
                InstallationId = Guid.CreateVersion7(),
                ConsumerId = runtime.ConsumerId,
                Mode = runtime.Mode,
                ExpectedRuntimeGeneration = runtime.Generation,
                ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
                ValidUntil = Now.AddMinutes(10)
            };
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, installation)).IsSuccess).IsTrue();
        }
        var record = new RecordModerationDeploymentSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch,
            SourceId = Guid.CreateVersion7(),
            PolicyId = publication.PolicyId,
            ExpectedPolicyRevision = publication.Revision,
            PublicationId = publication.Id,
            ProfileId = compiled.Profiles.Single().Definition.Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(change == Change.ConfigurationBoundsDeployment ? 8 : 5)
        };
        if (change != Change.MissingDeployment)
        {
            await Assert.That((await WriteDeploymentAsync(database, database.AdministratorId, record)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            epoch++;
        }
        var adopt = new AdoptModerationDeploymentSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = epoch,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(change == Change.ConfigurationBoundsDeployment ? 7 : 4)
        };
        if (change is not (Change.MissingDeployment or Change.MissingDeploymentAdoption))
        {
            await Assert.That((await WriteDeploymentAsync(database, database.UrgencyAssessorId, adopt)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            epoch++;
        }
        if (change == Change.WithdrawnDeploymentAdoption)
            await Assert.That((await WriteDeploymentAsync(database, database.AdministratorId, new WithdrawModerationDeploymentAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == Change.RevokedConfiguration)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationProfileSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch,
                AdoptionId = profileAdoptionId,
                ExpectedAdoptionRevision = 1,
                CaseId = target.CaseId,
                ExpectedCaseRevision = 1
            })).IsSuccess).IsTrue();
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            epoch = (await reader.ModerationQualificationEpochs.SingleAsync()).Revision;
        }
        await Assert.That((await database.ExecuteAsync(database.SubjectId, new RecordModerationCaseResponse
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch,
            EvidenceId = Guid.CreateVersion7(),
            CaseId = target.CaseId,
            ExpectedCaseRevision = 1,
            Statement = "The subject requests review without activating a restriction."
        })).IsSuccess).IsTrue();
        var missingReceiptKind = change switch
        {
            Change.MissingSelectionReceipt => ModerationQualificationSourceKind.ContributionSelection,
            Change.MissingProfileReceipt => ModerationQualificationSourceKind.ProfileAdoption,
            Change.MissingDeploymentReceipt => ModerationQualificationSourceKind.DeploymentSource,
            Change.MissingDeploymentAdoptionReceipt => ModerationQualificationSourceKind.DeploymentAdoption,
            Change.MissingInstallationReceipt => ModerationQualificationSourceKind.ConsumerInstallation,
            _ => (ModerationQualificationSourceKind?)null
        };
        if (missingReceiptKind is { } kind)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                .FirstAsync(row => row.SourceKind == kind));
            await writer.SaveChangesAsync();
        }
        if (change == Change.ChangedArtifact) runtimes[0].Artifact = new string('c', 64);
        if (change == Change.ChangedRuntime) runtimes[0].Generation = new string('d', 64);
        var clock = new JoinClock();
        await using var context = database.Context();
        await using var otherContext = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, original.Subject,
            database.Scope, Guid.CreateVersion7(), selection.EffectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationActivationProfileObservation? configuration = null;
        ModerationActivationProfileObservation? joined = null;
        var reads = 0;
        var entered = false;
        await store.ExecuteAsync(operation, async (lifecycle, token) =>
        {
            entered = true;
            var resolved = await participant.ResolveAsync(lifecycle, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await lifecycle.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            var transaction = context.Database.CurrentTransaction!;
            var (admin, machine) = Identity(context, database.SubjectId);
            var publications = new ModerationPolicyPublicationRepository(
                context, admin, machine, database.Inventory, null, clock, runtimes);
            // Acquire real native authority under the store's original lifecycle transaction.
            // Currentness delegates to that lifecycle's exact session/operation/grant; the
            // profile callback is the production reader, not an injected observation.
            var authority = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(operation, token);
            await Assert.That(authority).IsNotNull();
            using var evidence = new ModerationConsumerEvidenceSession(
                context, transaction, authority!, compiled, async cancellation =>
                {
                    var current = await participant.ResolveAsync(lifecycle, operation, cancellation);
                    return current.Outcome == ModerationOperationOutcome.Known
                        && ReferenceEquals(current.Grant, resolved.Grant);
                }, publications, database.Inventory, runtimes, null, clock,
                async (selectedPublication, actualDecision, at, cancellation) =>
                {
                    reads++;
                    if (reads == 2)
                    {
                        if (change == Change.ArtifactDriftsDuringConfigurationRead) runtimes[0].Artifact = new string('e', 64);
                        if (change == Change.ExpiryDuringRead) clock.UtcNow = Now.AddMinutes(4);
                        if (change is Change.ConfigurationDriftsDuringRead or Change.AuthorityDriftsDuringRead
                            or Change.AdoptionDriftsDuringRead)
                        {
                            if (change == Change.ConfigurationDriftsDuringRead)
                                context.ModerationQualificationMutationReceipts.Remove(
                                    await context.ModerationQualificationMutationReceipts.SingleAsync(
                                        row => row.SourceKind == ModerationQualificationSourceKind.ProfileAdoption, cancellation));
                            else if (change == Change.AuthorityDriftsDuringRead)
                                context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(
                                    row => row.UserId == database.UrgencyAssessorId, cancellation));
                            else
                                (await context.Set<ModerationDeploymentAdoption>().SingleAsync(cancellation))
                                    .Withdraw(1, database.AdministratorId, Now);
                            await context.SaveChangesAsync(cancellation);
                        }
                    }
                    var actual = await participant.AcquireProfileConfigurationAsync(transaction.TransactionId,
                        selectedPublication, actualDecision, resolved.Grant!, at, cancellation);
                    if (reads == 1) configuration = actual;
                    return actual;
                });
            var ownerContext = change == Change.OtherContext ? otherContext : context;
            var definitions = new ModerationPolicyDefinitionSourceOwner(ownerContext, admin, machine, database.Inventory, clock);
            var deploymentOwner = new ModerationDeploymentSourceOwner(ownerContext, definitions, database.Inventory, runtimes, clock);
            var owner = new ModerationProfileDeploymentSourceOwner(ownerContext, deploymentOwner, clock);
            var composition = new ModerationActivationSourceOwner(ownerContext, owner,
                new ModerationConstitutionSourceOwner(ownerContext, definitions, clock),
                new ModerationRecognitionPrivacySourceOwner(ownerContext, definitions, clock),
                new ModerationEventRightsSourceOwner(ownerContext, definitions, clock),
                new ModerationChildAgePolicySourceOwner(ownerContext, definitions, clock),
                new ModerationMeasuredOperationsSourceOwner(ownerContext, definitions, database.Inventory, clock),
                null, runtimes, clock);
            if (change == Change.ClosedBorrow) evidence.Dispose();
            if (change == Change.TenantBypass) context.EnableTenantFilterBypass("Native profile join isolation witness.");
            if (change == Change.ExpiredConfiguration) clock.UtcNow = Now.AddMinutes(5);
            if (change == Change.ExpiredDeploymentAdoption) clock.UtcNow = Now.AddMinutes(4);
            if (change == Change.ReplacedTransaction)
            {
                await transaction.RollbackAsync(token);
                await transaction.DisposeAsync();
                await using var replacement = await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, token);
                joined = await ReadProfileAsync(evidence, database.HostId, replacement.TransactionId,
                    compiled, compiled.Profiles.Single(), decision, Now, token);
            }
            else
            {
                using var publicationBorrow = change == Change.PublicationBorrow
                    ? publications.BorrowConsumerEvidence(authority!, compiled) : null;
                joined = await ReadProfileAsync(publicationBorrow ?? evidence,
                    change == Change.WrongHost ? Guid.CreateVersion7() : database.HostId,
                    change == Change.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
                    change == Change.OtherCompiledPolicy ? ModerationPolicyPublicationProjection.RestoreHistory(publication)! : compiled,
                    compiled.Profiles.Single(), change == Change.DetachedDecision ? original : decision, Now, token);
            }
            if (composed && change is Change.None or Change.ConfigurationBoundsDeployment)
            {
                var binding = compiled.Consumers.First().Binding;
                await Assert.That(await composition.ReadConformanceAsync(evidence, transaction.TransactionId,
                    binding, "resource.payload", Now, token)).IsNull();
                await Assert.That(await composition.ReadConformanceAsync(evidence, transaction.TransactionId,
                    binding, "unregistered.consumer", Now, token)).IsNull();
                await Assert.That(await composition.AcquireConsumerAsync(evidence, transaction.TransactionId,
                    binding, "unregistered.consumer", Now, token)).IsNull();
                await Assert.That(await composition.AcquirePrerequisiteAsync(evidence, transaction.TransactionId,
                    binding, (ModerationActivationPrerequisite)0, Now, token)).IsNull();
            }
            return ModerationOperationResult.Unavailable();

            Task<ModerationActivationProfileObservation?> ReadProfileAsync(
                IModerationConsumerEvidenceSession session, Guid hostId, Guid transactionId,
                Explore.Application.Features.Moderation.Policy.ModerationPolicyCompiledDraft policy,
                Explore.Application.Features.Moderation.Policy.ModerationPolicyCompiledProfile profile,
                ModerationDecision currentDecision, DateTimeOffset at, CancellationToken cancellation) =>
                composed
                    ? composition.AcquireProfileAsync(session, hostId, transactionId, policy, profile, currentDecision, at, cancellation)
                    : owner.AcquireProfileAsync(session, hostId, transactionId, policy, profile, currentDecision, at, cancellation);
        }, CancellationToken.None);
        await Assert.That(entered).IsTrue();
        if (change is Change.None or Change.ConfigurationBoundsDeployment)
        {
            await Assert.That(joined).IsNotNull();
            await Assert.That(configuration).IsNotNull();
            await Assert.That(joined!.SourceId).IsEqualTo(profileAdoptionId);
            await Assert.That(joined.SourceId == record.SourceId || joined.SourceId == adopt.AdoptionId).IsFalse();
            await Assert.That(joined.SourceRevision).IsEqualTo(configuration!.SourceRevision);
            await Assert.That(joined.WriterGeneration).IsEqualTo(configuration.WriterGeneration);
            await Assert.That(joined.ValidUntil).IsEqualTo(Now.AddMinutes(change == Change.None ? 4 : 5));
            await Assert.That(ModerationPolicyPublicationProjection.Digest(joined with { ValidUntil = configuration.ValidUntil }))
                .IsEqualTo(ModerationPolicyPublicationProjection.Digest(configuration));
            await Assert.That(reads).IsEqualTo(2);
        }
        else await Assert.That(joined).IsNull();
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await unchanged.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(original.Revision);
        await Assert.That(await unchanged.ModerationSanctions.AnyAsync()).IsFalse();
        await Assert.That(await unchanged.ModerationEffectIntents.AnyAsync()).IsFalse();
        await Assert.That(await unchanged.ModerationConsumerMeasurementReceipts.AnyAsync()).IsFalse();
        if (change is Change.None or Change.ConfigurationBoundsDeployment)
        {
            await Assert.That((await unchanged.ModerationProfileAdoptions.SingleAsync()).ValidUntil).IsEqualTo(Now.AddMinutes(5));
            await Assert.That((await unchanged.Set<ModerationDeploymentSource>().SingleAsync()).ValidUntil).IsEqualTo(record.ValidUntil);
            await Assert.That((await unchanged.Set<ModerationDeploymentAdoption>().SingleAsync()).ValidUntil).IsEqualTo(adopt.ValidUntil);
        }
    }

    private static async Task<ModerationQualificationWriteResult> WriteDeploymentAsync(
        SourceDatabase database, Guid actor, ModerationQualificationMutation mutation)
    {
        await using var context = database.Context();
        var (admin, machine) = Identity(context, actor);
        var clock = new JoinClock();
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory,
            null, clock, database.ConsumerRuntimeSources);
        var writer = new ModerationQualificationRepository(context, admin, machine, publications,
            new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock), clock, database.Privacy.Authority(context, clock),
            database.ConsumerRuntimeSources, deploymentInventory: database.Inventory);
        return await writer.ExecuteAsync(mutation);
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context, Guid actor)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "profile-deployment-witness"))
            }
        };
        return (new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context),
            new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance), new MachinePrincipalAccessor(http));
    }

    private sealed class JoinClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class HostRuntime(string consumerId, string inventory) : IModerationConsumerRuntimeSource
    {
        public string ConsumerId => consumerId;
        public ModerationNativeMode Mode => ModerationNativeMode.Embedded;
        public string Generation { get; set; } = new('a', 64);
        public string Artifact { get; set; } = new('b', 64);
        public ModerationConsumerRuntimeObservation ReadCurrent() => new(ConsumerId, Mode, Generation, inventory, Artifact);
    }
}
