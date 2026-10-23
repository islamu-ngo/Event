using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeCleanupTests
{
    private sealed class CleanupDatabase(
        SourceDatabase database, ModerationPolicyPublicationRequest publication, Guid publicationId) : IAsyncDisposable
    {
        public SourceDatabase Database { get; } = database;
        public CleanupClock Clock { get; } = new();
        public ModerationNoticePrivacyFixture Privacy { get; private set; } = null!;
        public string KeyId { get; } = ApiKeyHashing.CreateKeyId();
        private string Credential { get; set; } = string.Empty;

        public static async Task<CleanupDatabase> CreateAsync()
        {
            var database = await SourceDatabase.CreateAsync();
            var publication = database.Publication(1);
            var published = await database.PublishAsync(database.AdministratorId, publication);
            if (published.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("Native cleanup fixture publication failed.");
            var fixture = new CleanupDatabase(database, publication, published.Publication!.Id)
            {
                Privacy = await ModerationNoticePrivacyFixture.CreateAsync()
            };
            await using var context = database.Context();
            var host = await context.ModerationHostBindings.SingleAsync();
            var secret = ApiKeyHashing.CreateSecret();
            fixture.Credential = ApiKeyHashing.FormatPersistedApiKey(fixture.KeyId, secret);
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = fixture.KeyId, Name = "Original notice cleanup",
                SecretHash = ApiKeyHashing.ComputeHash(secret), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = database.AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
            });
            context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, fixture.KeyId,
                null, ExternalApiKeyOwnerType.InstanceAdmin, database.AdministratorId, host.MaintenanceServicePrincipalId));
            await context.SaveChangesAsync();
            return fixture;
        }

        public ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            var context = Database.Context();
            if (interceptors.Length == 0) return context;
            var options = (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>();
            context.Dispose();
            return new(new DbContextOptionsBuilder<ExploreDbContext>(options).AddInterceptors(interceptors).Options)
                { TenantContext = Database };
        }

        public async Task<MachinePrincipalAccessor> AuthenticateAsync(ExploreDbContext context)
        {
            var credential = await new ModerationReconciliationCredentialProvider(
                new CleanupOptions(new() { Enabled = true, TenantId = Database.TenantId }),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new CleanupSecret(Credential), Clock)
                .AuthenticateAsync(Database.TenantId, CancellationToken.None);
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machine.SetPrincipal(credential ?? throw new InvalidOperationException("Native cleanup authentication failed."));
            return machine;
        }

        public ModerationNoticeCleanupRepository Repository(
            ExploreDbContext context, MachinePrincipalAccessor machine, bool nativeNotificationOwner = false)
        {
            var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
            var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), Clock);
            return new(context, machine, bindings, keys, Clock, nativeNotificationOwner
                ? new NotificationIntentRepository(context,
                    new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), Clock)
                : null);
        }

        public async Task<Guid> SeedAsync(
            bool acknowledged = false, string? defect = null, bool nativePointer = false,
            Guid? approvalId = null, DateTimeOffset? retainUntil = null, bool instanceScoped = false)
        {
            var deadline = retainUntil ?? Ceiling;
            var jurisdiction = instanceScoped ? Database.Instance : Database.Scope;
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, Database.SubjectId, Database.Instance);
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Database.AdministratorId, Database.Instance);
            var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Database.ReviewerId, Database.Instance);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, jurisdiction, Guid.CreateVersion7(), OriginalAt);
            var profile = publication.Proposed.Profiles.Single();
            var rule = profile.Rule;
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
                allegation.Scope, null, null, rule, rule, rule, rule,
                reviewer, ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, OriginalAt),
                "Independently retained private case evidence.", OriginalAt);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                Database.Instance, jurisdiction, rule, [ModerationCapability.Publish],
                OriginalAt, OriginalAt.AddDays(1), null, 1, OriginalAt);
            decision.RecordResponse(1, OriginalAt);
            decision.AcceptFinding(finding.Id, 2, OriginalAt);
            decision.Approve(reviewer, jurisdiction, 3, OriginalAt);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, OriginalAt);
            var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
                decision.Id, decision.Revision, sanction.Id, sanction.Revision, Database.Scope.TrustDomainId,
                (int)jurisdiction.Kind, jurisdiction.Id, (int)subject.Kind, subject.Id,
                (int)ModerationOperationKind.Activate, 1, 2, OriginalAt, null, null);
            var text = new ModerationNoticeSafeText("Original independently approved safe explanation.");
            var intent = new ModerationNoticeDisclosureIntent(approvalId ?? Guid.CreateVersion7(), effect.Id,
                decision.Id, decision.Revision, allegation.Id, 1, publication.Proposed.PolicyId, 1, publicationId,
                profile.Id, Guid.CreateVersion7(), new string('a', 64),
                text, text, [text], text, text, text, text, OriginalAt.AddMinutes(1), deadline);
            var approval = ModerationNoticeDisclosureApproval.Approve(intent, Database.HostId,
                Database.Scope.TrustDomainId, Database.TenantId, Database.AdministratorId, 1, OriginalAt);
            var scope = ModerationNoticeScope.From(jurisdiction);
            var origin = ModerationNoticeScope.From(Database.Instance);
            var originalRule = new ModerationNoticeRule(rule.OwnerId, rule.Code, rule.Revision);
            var stamp = new ModerationNoticeSourceStamp(Guid.CreateVersion7(), 1, "event.moderation.original-cleanup-witness");
            var target = new ModerationNoticeTarget(scope, subject.Kind, subject.Id, Guid.CreateVersion7(),
                1, "event.publish", null, "event.publication", stamp);
            var interval = new ModerationReferenceInterval(
                ModerationReferenceTimingKind.Finite, OriginalAt, OriginalAt.AddDays(1), null, null);
            var modes = new ModerationConsequenceModes(
                ModerationContentMode.PendingPublicationReview, ModerationManagementMode.CurrentAuthority,
                ModerationResourceReadMode.CurrentAudience, ModerationNewAdmissionMode.CurrentEligibility,
                ModerationExistingAdmissionMode.CurrentEligibility, ModerationPrivateDisclosureMode.CurrentAudience,
                ModerationSettlementMode.NoBookingChange, ModerationCapacityMode.NoSeatChange,
                ModerationCredentialMode.CurrentCredentialLifecycle, ModerationNotificationMode.DurableSafeSubjectAndAdmin,
                ModerationRecognitionMode.NoCaptureFromThisControl, ModerationPendingWorkMode.CurrentPublicationApprovalRequired,
                ModerationRestorationMode.RecomputeCurrentRightsOnly);
            var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
            var originalNotice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, decision.Id,
                decision.Revision, decision.State, sanction.Id, sanction.Revision, sanction.State, null, null,
                effect.OperationKind, scope, subject.Kind, subject.Id, origin, intent.PolicyId, 1, publicationId, 1,
                profile.Id, reference.Id, reference.Revision, stamp, approval.Id, 1,
                intent.ContributionId, originalRule, originalRule, text, text, [text], text, text, text, text,
                [], [new(null, null, target, interval, modes)], OriginalAt, deadline, 6);
            var original = new ModerationNoticeOriginalDecision(decision.Id, decision.Revision, allegation.Id,
                decision.State, scope, subject.Kind, subject.Id, origin, originalRule, decision.CapabilityMask,
                OriginalAt, OriginalAt.AddDays(1), null, finding.Id, 1, sanction.Id, null);
            var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
                Database.HostId, Database.Instance.Parent!.Id, Database.Instance.Id,
                defect == "wrong-writer" ? Guid.CreateVersion7() : Database.TenantId,
                Database.EpochId, 2, original, new(finding.Id, 1, finding.State, originalRule), originalNotice,
                [new(Guid.CreateVersion7(), 1, Database.ReviewerId, Database.ReviewerId,
                    Guid.CreateVersion7(), 1, Database.UrgencyAssessorId, Guid.CreateVersion7(), 1,
                    Database.UrgencyAssessorId, OriginalAt.AddMinutes(30))], OriginalAt.AddMinutes(1), stamp);
            var source = ModerationNoticeSource.Capture(effect, decision.ScopeTenantId, JsonSerializer.Serialize(capture),
                defect == "retention" ? deadline.AddDays(1) : deadline);
            var noticeId = Guid.CreateVersion7();
            var nativeId = nativePointer ? Guid.CreateVersion7() : (Guid?)null;
            var occurrence = ModerationNotice.Create(noticeId, source, ModerationNoticeAudience.Subject,
                1, Database.SubjectId, nativeId, nativePointer
                    ? ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable
                    : ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
            var reviewerOccurrence = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                1, Database.ReviewerId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
            var receipt = ModerationOperationReceipt.Complete(effect.OperationId, new string('b', 64),
                effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
                Database.AdministratorId, new string('c', 64), decision.Id, sanction.Id,
                defect == "wrong-operation-receipt" ? decision.Revision + 1 : decision.Revision, 1, 2, OriginalAt);
            await using var context = Context();
            context.AddRange(allegation, finding, decision, sanction, receipt, effect, approval);
            if (defect != "missing-source")
            {
                context.Add(source);
                if (defect != "missing-subject") context.Add(occurrence);
                if (defect != "missing-reviewer") context.Add(reviewerOccurrence);
                if (acknowledged)
                    context.Add(ModerationNoticeNotificationReceipt.Acknowledge(Guid.CreateVersion7(), occurrence, source,
                        Database.TenantId, Database.SubjectId, source.SnapshotHash, 6, deadline, OriginalAt));
            }
            if (nativeId is { } id)
                context.NotificationIntents.Add(new NotificationIntent
                {
                    Id = id, TenantId = Database.TenantId, CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration,
                    OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
                    RecipientKindId = (int)NotificationRecipientKindEnum.User, StatusId = (int)NotificationIntentStatusEnum.Pending,
                    RecipientUserId = Database.SubjectId, TemplateKey = "moderation.notice.portal.v1",
                    DeduplicationKey = $"moderation-notice:{effect.Id:N}:1:{Database.SubjectId:N}",
                    SafePayloadReference = $"moderation-notice:{noticeId:D}", SafePayloadHash = source.SnapshotHash,
                    CreatedAt = OriginalAt.UtcDateTime
                });
            await context.SaveChangesAsync();
            if (defect == "wrong-hash")
            {
                context.ModerationTrustDomainId = Database.Instance.TrustDomainId;
                await context.ModerationNoticeSources.Where(row => row.EffectId == effect.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotHash, new string('d', 64)));
            }
            return effect.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class CleanupClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Ceiling;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CleanupOptions(ModerationReconciliationOptions options) : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => options;
        public ModerationReconciliationOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }

    private sealed class CleanupSecret(string credential) : IModerationReconciliationSecretProvider
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(credential);
        }
    }
}
