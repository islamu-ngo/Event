using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeEmailTests
{
    private sealed class EmailFixture(NoticeDatabase notice, SqliteConnection connection,
        AuthorityFactory authorityFactory) : IAsyncDisposable
    {
        public NoticeDatabase Notice { get; } = notice;
        public string CurrentAddress { get; private set; } = "original-notice@example.invalid";
        public Guid OutboxId { get; private set; }
        private Guid RecipientId { get; set; }

        public static async Task<EmailFixture> CreateAsync(bool reviewer = false, bool muted = false, bool content = false)
        {
            var notice = await NoticeDatabase.CreateAsync(content: content);
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            var factory = new AuthorityFactory(options);
            await using (var retained = factory.CreateDbContext())
                await retained.Database.EnsureCreatedAsync();
            var fixture = new EmailFixture(notice, connection, factory)
            {
                RecipientId = reviewer ? notice.Database.ReviewerId : notice.Database.SubjectId
            };
            await using var context = notice.Database.Context();
            await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
            await fixture.SetAddressAsync(context, fixture.CurrentAddress);
            if (muted)
                await new NotificationPreferenceProfileRepository(context).UpsertUserMuteAsync(
                    notice.Database.TenantId, fixture.RecipientId, true);
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = notice.Database.Scope.TrustDomainId;
            var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync();
            var noticeId = reviewer ? notice.ReviewerNoticeId : notice.SubjectNoticeId;
            var original = await context.ModerationNotices.SingleAsync(row => row.Id == noticeId);
            var user = await context.Users.Include(row => row.Pii).Include(row => row.IdentityEmailClaims)
                .ThenInclude(row => row.Evidence).SingleAsync(row => row.Id == fixture.RecipientId);
            var unit = new EfCoreUnitOfWork(context);
            var policyLock = new RelationalSettingMutationLock(context, unit);
            var graph = new RecipientNotificationMaterializer(
                new NotificationIntentRepository(context, policyLock, notice.Clock), unit, policyLock,
                new PrivacyErasureStateRepository(context));
            var producer = new ModerationNoticeNativeMaterializer(
                new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions())),
                new NotificationPreferenceResolver(context), graph);
            await policyLock.ExecuteOrderedGroupsAsync([[Explore.Domain.Constants.GovernanceSettingKeys.Email.DeliveryEnabled]],
                token => unit.ExecuteInTransactionAsync(async ct =>
                {
                    var result = await producer.MaterializeAsync(source, original.Id,
                        original.Audience, user, ct);
                    // The reusable portal fixture predates its optional native projection.
                    // Bind the real producer's graph to the same original occurrence, never a replacement recipient.
                    context.Remove(original);
                    await context.SaveChangesAsync(ct);
                    context.Add(ModerationNotice.Create(original.Id, source, original.Audience,
                        original.RecipientSubjectKind, original.RecipientSubjectId, result.IntentId, result.Disposition));
                    await context.SaveChangesAsync(ct);
                    return true;
                }, token), CancellationToken.None);
            fixture.OutboxId = await context.EmailDispatchOutbox.Select(row => row.Id).SingleOrDefaultAsync();
            return fixture;
        }

        public async Task<ModerationNoticeEmailRequest> AdmitAsync()
        {
            await using var context = Notice.Database.Context();
            var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
            var lease = Guid.CreateVersion7();
            var repository = new EmailDispatchOutboxRepository(context);
            var claimed = await repository.TryClaimSpecificAsync(new(
                Notice.Database.TenantId, dispatch.PublishEventId, lease, 10, 5, 100, 50,
                Notice.Clock.GetUtcNow().UtcDateTime), CancellationToken.None);
            if (claimed is null) throw new InvalidOperationException("The native email claim was unavailable.");
            var admission = await EmailDispatchSqliteFixture.CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
                new(Notice.Database.TenantId, dispatch.Id, lease, claimed.AttemptCount, 60, 60,
                    "original-notice-witness", Notice.Clock.GetUtcNow().UtcDateTime));
            if (admission.Outcome != EmailDispatchEligibilityOutcome.Eligible)
                throw new InvalidOperationException($"The native handoff was unavailable: {admission.SkipReason}.");
            return new(Notice.Database.TenantId, dispatch.Id, lease, admission.AttemptNumber!.Value);
        }

        public async Task<EmailResult> SendAsync(ModerationNoticeEmailRequest request,
            Func<EmailMessage, CancellationToken, Task<EmailResult>> send)
        {
            await using var context = Notice.Database.Context();
            return await CreateAuthority(context).SendAsync(request, send, CancellationToken.None);
        }

        public ModerationNoticeEmailAuthority CreateAuthority(ExploreDbContext context)
        {
            var http = new HttpContextAccessor();
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var activation = new ModerationNativeActivationQualification(context, Notice.Database.Inventory,
                null, new ModerationQualificationPolicySourceOwner(context, Notice.Clock), Notice.Clock);
            var privacy = Authority(context);
            var read = new ModerationNoticeReadRepository(context, admin, new MachinePrincipalAccessor(http),
                new ModerationNoticeRecipientRepository(context), activation, Notice.Clock, privacy);
            return new ModerationNoticeEmailAuthority(context, read, privacy,
                new NotificationPreferenceResolver(context));
        }

        public EmbeddedPrivacyErasureAuthorityRepository Authority(ExploreDbContext context) =>
            new(authorityFactory, Notice.Clock, Options.Create(new PrivacyErasureOptions()), applicationContext: context);

        public async Task ChangeAddressAsync()
        {
            await using var context = Notice.Database.Context();
            CurrentAddress = "current-notice@example.invalid";
            await SetAddressAsync(context, CurrentAddress);
        }

        private async Task SetAddressAsync(ExploreDbContext context, string address)
        {
            foreach (var evidence in await context.UserIdentityEmailEvidence
                .Where(row => row.UserId == RecipientId).ToArrayAsync())
                evidence.Invalidate();
            var user = await context.Users.Include(row => row.Pii).SingleAsync(row => row.Id == RecipientId);
            user.Pii!.Email = address;
            var binding = new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Google,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    "https://accounts.google.com", Guid.CreateVersion7().ToString("N")).Value,
                CreatedAt = Notice.Clock.GetUtcNow().UtcDateTime
            };
            var claim = UserIdentityEmailClaim.Create(user.Id, address);
            context.AddRange(binding, claim, UserIdentityEmailEvidence.Create(
                user.Id, claim.Id, binding.Id, Notice.Clock.GetUtcNow().UtcDateTime));
            await context.SaveChangesAsync();
        }

        public async Task InvalidateAsync(string change)
        {
            if (change == "deadline")
            {
                Notice.Clock.UtcNow = Notice.Approval.RetainUntil;
                return;
            }
            await Notice.MutateAsync(async context =>
            {
                switch (change)
                {
                    case "source":
                        await context.ModerationNoticeSources.ExecuteUpdateAsync(
                            update => update.SetProperty(row => row.SnapshotHash, new string('0', 64)));
                        break;
                    case "approval":
                        (await context.ModerationNoticeDisclosureApprovals.SingleAsync())
                            .Revoke(Notice.Approval.Revision, Notice.Database.AdministratorId, Notice.Clock.GetUtcNow());
                        break;
                    case "recipient":
                        (await context.Users.SingleAsync(row => row.Id == Notice.Database.SubjectId)).IsDeleted = true;
                        break;
                    case "preference":
                        await new NotificationPreferenceProfileRepository(context).UpsertUserMuteAsync(
                            Notice.Database.TenantId, Notice.Database.SubjectId, true);
                        break;
                    case "email":
                        foreach (var evidence in await context.UserIdentityEmailEvidence.ToArrayAsync())
                            evidence.Invalidate();
                        break;
                    case "tenant":
                        (await context.TenantUsers.SingleAsync(row => row.UserId == Notice.Database.SubjectId)).IsDeleted = true;
                        break;
                    case "hash":
                        (await context.NotificationIntents.SingleAsync()).SafePayloadHash = new string('0', 64);
                        break;
                    case "template":
                        (await context.EmailDispatchOutbox.SingleAsync()).PlainTextBody = Notice.PrivateJustification;
                        break;
                    case "pointer":
                        (await context.EmailDispatchOutbox.SingleAsync()).SourceId = Notice.ReviewerNoticeId;
                        break;
                    case "kind":
                        (await context.EmailDispatchOutbox.SingleAsync()).Kind = EmailDispatchKind.ModerationAvailabilityRequired;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change));
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            await Notice.DisposeAsync();
        }
    }

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }
}
