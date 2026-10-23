using System.Data;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Maintenance_fences_governance_participants_without_borrowing_their_human_permission(bool withdraw)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var request = database.Request(ModerationReferenceProfileId.CFG05, ModerationReferenceProfileId.CFG28,
            ModerationReferenceProfileId.CFG11);
        request = request with { Proposed = request.Proposed with
        {
            Profiles = request.Proposed.Profiles.SetItem(0, request.Proposed.Profiles[0] with
            { Recognition = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction })
        }};
        await Assert.That((await database.Writer(context).PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var clock = new DeploymentClock();
        var humans = Enumerable.Range(0, 10).Select(_ => Guid.CreateVersion7()).ToArray();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in humans)
        {
            var user = new User { Id = id, Pii = new UserPii
            {
                UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Maintenance", LastName = "Governance"
            }};
            context.Users.Add(user);
            context.PlatformUserRoles.Add(new PlatformUserRole
                { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var (admin, humanMachine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, humanMachine,
            database.Inventory, null, clock);
        ModerationQualificationRepository Writer(Guid human)
        {
            var identity = DeploymentIdentity(context, human);
            return new(context, identity.Admin, identity.Machine, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, database.Privacy.Authority(context, clock), deploymentInventory: database.Inventory);
        }
        var availability = new RecordModerationConstitutionAvailability
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            AvailabilityId = Guid.CreateVersion7(), MaximumCaseload = 2, ValidUntil = Now.AddMinutes(8)
        };
        await Assert.That((await Writer(humans[0]).ExecuteAsync(availability)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var terms = new ModerationConstitutionTerms(ModerationScopeKind.Tenant,
            ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
            ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
            ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
            ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
            ModerationConstitutionOverload.AcknowledgeAndEscalate, 1, 6,
            TimeSpan.FromDays(30), TimeSpan.FromHours(1),
            "https://example.invalid/support", "https://example.invalid/redress", humans[1], humans[2]);
        var source = new RecordModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles[0].Id,
            Mode = ModerationNativeMode.Embedded, Terms = terms, ValidUntil = Now.AddMinutes(5),
            Reviewers = [new(availability.AvailabilityId, 1, 3, humans[0], humans[0],
                "The native reviewer controls their own account.", "Accountable qualification assessment.",
                "Separate controller and recorded independent oversight.")]
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(source)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adoption = new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            SourceId = source.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 4,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(humans[3]).ExecuteAsync(adoption)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (withdraw)
            await Assert.That((await Writer(humans[2]).ExecuteAsync(new WithdrawModerationConstitutionAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
                AdoptionId = adoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);

        var privacySource = new RecordModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = withdraw ? 6 : 5,
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles[0].Id,
            Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddMinutes(5),
            Terms = RecognitionPrivacyTerms(),
            Ownership = new(humans[4], humans[4], humans[5], humans[5], database.AdminId,
                "Distinct native governance, privacy and recovery controllers.",
                "Appointed privacy owner of recognition purposes, retention and protected correction.",
                "Appointed owner of finite recovery and lawful-request disposition."),
            Retention =
            [
                new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero)
            ]
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(privacySource)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var privacyAdoption = new AdoptModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = privacySource.ExpectedQualificationRevision + 1,
            SourceId = privacySource.SourceId, ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = privacySource.ExpectedQualificationRevision + 1,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(humans[4]).ExecuteAsync(privacyAdoption)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (withdraw)
            await Assert.That((await Writer(humans[5]).ExecuteAsync(new WithdrawModerationRecognitionPrivacyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = privacyAdoption.ExpectedQualificationRevision + 1,
                AdoptionId = privacyAdoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);

        var childSource = new RecordModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = privacyAdoption.ExpectedQualificationRevision + (withdraw ? 2 : 1),
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles[1].Id,
            Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddMinutes(5),
            Terms = ChildAgeTerms(),
            Ownership = new(humans[6], humans[6], humans[7], humans[7], database.AdminId,
                "Distinct native governance, age collection and independent review controllers.",
                "Appointed privacy/legal reviewer of minimum lawful age collection and correction.",
                "Appointed independent child-safeguarding reviewer of the accepted age and classification rules.",
                "Appointed collection owner for gated actual-subject threshold evidence.")
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(childSource)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var childAdoption = new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = childSource.ExpectedQualificationRevision + 1,
            SourceId = childSource.SourceId, ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = childSource.ExpectedQualificationRevision + 1,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(humans[6]).ExecuteAsync(childAdoption)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (withdraw)
            await Assert.That((await Writer(humans[7]).ExecuteAsync(new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = childAdoption.ExpectedQualificationRevision + 1,
                AdoptionId = childAdoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);

        var rightsSource = new RecordModerationEventRightsSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = childAdoption.ExpectedQualificationRevision + (withdraw ? 2 : 1),
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles[2].Id,
            Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddMinutes(5),
            Terms = EventRightsTerms(),
            Ownership = new(humans[6], humans[6], humans[4], humans[4], humans[5], humans[5], database.AdminId,
                "Distinct native governance, rights reviewer and mapping controllers.",
                "Appointed independent reviewer of event rights and premises obligations.",
                "Accountable continuity mapping governance, not event host authority.",
                "Accountable settlement mapping governance, not merchant authority.")
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(rightsSource)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var rightsAdoption = new AdoptModerationEventRightsSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = rightsSource.ExpectedQualificationRevision + 1,
            SourceId = rightsSource.SourceId, ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = rightsSource.ExpectedQualificationRevision + 1,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(humans[6]).ExecuteAsync(rightsAdoption)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (withdraw)
            await Assert.That((await Writer(humans[5]).ExecuteAsync(new WithdrawModerationEventRightsAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = rightsAdoption.ExpectedQualificationRevision + 1,
                AdoptionId = rightsAdoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);

        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var policy = ModerationPolicyPublicationProjection.RestoreHistory(
            await context.ModerationPolicyPublications.AsNoTracking().Include(row => row.Profiles)
                .SingleAsync(row => row.Id == request.Proposed.PublicationId))!;
        var measured = new RecordModerationMeasuredOperationsSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = rightsAdoption.ExpectedQualificationRevision + (withdraw ? 2 : 1),
            SourceId = Guid.CreateVersion7(), ProfileId = request.Proposed.Profiles[0].Id,
            Binding = policy.Consumers.First(row => row.Binding.Rule == request.Proposed.Profiles[0].Rule).Binding,
            ReviewerHumanId = humans[8], ReviewerControllerHumanId = humans[8], RecorderControllerHumanId = humans[9],
            ValidUntil = Now.AddMinutes(3)
        };
        await Assert.That((await Writer(humans[9]).ExecuteAsync(measured)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var acceptance = new AcceptModerationMeasuredOperationsSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = measured.ExpectedQualificationRevision + 1,
            SourceId = measured.SourceId, ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = measured.ExpectedQualificationRevision + 1,
            AcceptanceId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(2)
        };
        await Assert.That((await Writer(humans[8]).ExecuteAsync(acceptance)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (withdraw)
            await Assert.That((await Writer(humans[7]).ExecuteAsync(new WithdrawModerationMeasuredOperationsAcceptance
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = acceptance.ExpectedQualificationRevision + 1,
                AcceptanceId = acceptance.AcceptanceId, ExpectedAcceptanceRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        // Adversarial discovery pointer: this draft is NOT an active publication or permission.
        await context.ModerationPolicySelections.Where(row => row.Id == request.Proposed.PolicyId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ActivePublicationId, request.Proposed.PublicationId));
        var host = await context.ModerationHostBindings.SingleAsync();
        var keyId = ApiKeyHashing.CreateKeyId();
        var secret = ApiKeyHashing.CreateSecret();
        context.ExternalApiKeys.Add(new ExternalApiKey
        {
            Id = Guid.CreateVersion7(), KeyId = keyId, Name = "Governance maintenance",
            SecretHash = ApiKeyHashing.ComputeHash(secret), Scopes = "events:read",
            OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = database.AdminId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
        });
        context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
            Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, keyId, null,
            ExternalApiKeyOwnerType.InstanceAdmin, database.AdminId, host.MaintenanceServicePrincipalId));
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, database.Scope.Parent!);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, database.Scope, Guid.CreateVersion7(), Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, subject,
            database.Scope.Parent!, database.Scope, request.Proposed.Profiles[0].Rule,
            [ModerationCapability.Publish], Now, Now.AddDays(1), null, 1, Now);
        context.AddRange(allegation, decision);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
        var credential = await new ModerationReconciliationCredentialProvider(
            new GovernanceMaintenanceSettings(new() { Enabled = true, TenantId = database.TenantId }),
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new GovernanceMaintenanceSecret(ApiKeyHashing.FormatPersistedApiKey(keyId, secret)), clock)
            .AuthenticateAsync(database.TenantId, default);
        await Assert.That(credential).IsNotNull();
        machine.SetPrincipal(credential!);
        var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
            new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), clock);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var operation = ModerationOperation.Reconcile(subject, database.Scope, Guid.CreateVersion7(),
            Guid.CreateVersion7(), decision.Id, 1, 1, Guid.CreateVersion7(), 1);
        var authority = await new ModerationMaintenanceAuthority(context, machine, bindings, keys, clock)
            .AcquireAsync(operation, default);
        await Assert.That(authority).IsNotNull();
        await Assert.That(authority!.UserId).IsEqualTo(host.MaintenanceServicePrincipalId);
        await Assert.That(authority.UserId).IsNotEqualTo(database.AdminId);
        foreach (var human in humans.Append(database.AdminId))
            await Assert.That(authority.FencedHumans.Contains(human)).IsTrue();
        await Assert.That(await publications.AcquireSelectedAsync(transaction.TransactionId, database.HostId,
            database.Scope, request.Proposed.PolicyId, activeOnly: true)).IsNull();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.AnyAsync()).IsFalse();
    }

    private sealed class GovernanceMaintenanceSettings(ModerationReconciliationOptions settings)
        : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => settings;
        public ModerationReconciliationOptions Get(string? name) => settings;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }

    private sealed class GovernanceMaintenanceSecret(string credential) : IModerationReconciliationSecretProvider
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(credential);
        }
    }
}
