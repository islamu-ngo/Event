using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DomainEvent = Explore.Domain.Event;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentNoticeCommitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private const string OriginalTitle = "Original Content committing-writer custody";
    private const string ChangedTitle = "Disclosure dependency changed in adopted transaction";

    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task Adopted_authority_final_disclosure_flush_expiry_rolls_back_original_approval(RoleEnum role)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = await PrepareAsync(database, role);
        var checkpoint = new CommitCheckpoint(database, expireAtFlush: true);
        await using (var context = Context(database, checkpoint))
        {
            var authority = await AuthorityAsync(context, checkpoint);
            var result = await Writer(database, context, authority).ExecuteAsync(source);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        }
        await Assert.That(checkpoint.ApprovalSaved).IsTrue();
        await Assert.That(checkpoint.FlushCount).IsEqualTo(1);
        await Assert.That(checkpoint.Committed.Count).IsEqualTo(0);
        await Assert.That(database.EvaluationTime).IsEqualTo(Now.AddMinutes(5));
        await Assert.That(database.EvaluationTime < source.Intent.ValidUntil).IsTrue();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
        await Assert.That((await reader.Events.SingleAsync()).Title).IsEqualTo(OriginalTitle);
        await Assert.That((await reader.Set<EventDiscoveryRevision>().SingleAsync()).DisclosureEpoch)
            .IsEqualTo(checkpoint.DisclosureBefore);
        await AssertNoConsequencesAsync(reader);
    }

    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task Adopted_authority_commit_and_replay_preserve_one_original_approval_receipt_and_retention(RoleEnum role)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = await PrepareAsync(database, role);
        var checkpoint = new CommitCheckpoint(database, expireAtFlush: false);
        await using (var context = Context(database, checkpoint))
        {
            var authority = await AuthorityAsync(context, checkpoint);
            var writer = Writer(database, context, authority);
            var accepted = await writer.ExecuteAsync(source);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That(checkpoint.Committed.Count).IsEqualTo(1);
            await Assert.That(ReferenceEquals(checkpoint.Committed[0], checkpoint.ApprovalTransaction)).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            var replay = await writer.ExecuteAsync(source);
            await Assert.That(replay.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That(replay.Receipt!.Id).IsEqualTo(accepted.Receipt!.Id);
            await Assert.That(replay.Receipt.CompletedAt).IsEqualTo(accepted.Receipt.CompletedAt);
            await Assert.That(checkpoint.Committed.Count).IsEqualTo(2);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await Assert.That(checkpoint.ApprovalSaved).IsTrue();
        await Assert.That(checkpoint.FlushCount).IsEqualTo(1);
        await using var reader = Read(database);
        var approval = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(approval.Id).IsEqualTo(source.Intent.ApprovalId);
        await Assert.That(approval.Revision).IsEqualTo(1L);
        await Assert.That(approval.ApprovedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(approval.Intent.ContentRepresentatives.Single())
            .IsEqualTo(source.Intent.ContentRepresentatives.Single());
        await Assert.That(approval.RetainUntil).IsEqualTo(source.Intent.RetainUntil);
        await Assert.That(approval.IsCurrentAt(source.Intent, Now)).IsTrue();
        var receipt = await reader.ModerationQualificationMutationReceipts
            .SingleAsync(row => row.Id == source.OperationId);
        await Assert.That(receipt.SourceId).IsEqualTo(approval.Id);
        await Assert.That(receipt.ResultRevision).IsEqualTo(1L);
        await Assert.That(receipt.QualificationRevision).IsEqualTo(4L);
        await Assert.That(receipt.PrincipalId).IsEqualTo(database.AdministratorId);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That((await reader.Events.SingleAsync()).Title).IsEqualTo(ChangedTitle);
        await Assert.That((await reader.Set<EventDiscoveryRevision>().SingleAsync()).DisclosureEpoch)
            .IsEqualTo(checkpoint.DisclosureBefore + 1);
        await AssertNoConsequencesAsync(reader);
    }

    private static async Task AssertNoConsequencesAsync(ExploreDbContext reader)
    {
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationNotices.AnyAsync()).IsFalse();
        await Assert.That(await reader.PrivacyErasureSagas.AnyAsync()).IsFalse();
    }

    private static async Task<ApproveModerationNoticeDisclosureSource> PrepareAsync(SourceDatabase database, RoleEnum role)
    {
        DomainEvent owningEvent;
        EventRoleAssignment assignment;
        await using (var context = database.Context())
        {
            var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
            var actorId = Guid.CreateVersion7();
            var actor = new Actor
            {
                Id = actorId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = database.SubjectId,
                Pii = new ActorPii { ActorId = actorId, DisplayName = "Original Content representative" }
            };
            owningEvent = new(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                ActorId = actor.Id,
                Actor = actor,
                Title = OriginalTitle,
                SubmittedByUserId = database.SubjectId,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Private,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventStatus = null!
            };
            assignment = EventRoleAssignment.Create(tenant.Id, owningEvent.Id, database.SubjectId,
                (int)role, EventRoleAssignmentStatus.Active,
                Now.AddHours(-1).UtcDateTime, Now.AddMinutes(5).UtcDateTime, database.AdministratorId);
            context.AddRange(actor, owningEvent, assignment);
            await context.SaveChangesAsync();
        }
        var opened = database.OpenCase() with
        {
            SubjectKind = ModerationSubjectKind.Content,
            SubjectId = owningEvent.Id
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(2, ModerationReferenceProfileId.CFG09);
        request = request with
        {
            Proposed = request.Proposed with
            {
                Profiles = [request.Proposed.Profiles.Single() with { SubjectKinds = [ModerationSubjectKind.Content] }]
            }
        };
        var published = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        // Seed only the accepted-decision boundary, as in the existing custody fixture.
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(ModerationSubjectKind.Content, owningEvent.Id, database.Scope),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish],
            Now, Now.AddHours(1), null, 1, Now);
        await using (var context = Read(database))
        {
            context.ModerationDecisions.Add(decision);
            await context.SaveChangesAsync();
        }
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            Intent = new ModerationNoticeDisclosureIntent(
                Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 2, opened.CaseId, 1,
                request.Proposed.PolicyId, 1, published.Publication!.Id, profile.Id,
                Guid.CreateVersion7(), new string('a', 64),
                new(request.Proposed.Classes.Single().Meaning), new(request.Proposed.Reasons.Single().Meaning),
                [new("Original Content explanation.")], new("Protected response."),
                new("Independent review."), new("No automated finding."), new("Purpose-bound retention."),
                Now.AddMinutes(10), Now.AddYears(1))
            {
                ContentRepresentatives = [new(assignment.Id, assignment.Version, database.SubjectId)]
            }
        };
    }

    private static ExploreDbContext Context(SourceDatabase database, CommitCheckpoint checkpoint,
        params IInterceptor[] interceptors)
    {
        using var original = database.Context();
        var options = (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>();
        return new(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(new EnrollDisclosureDependency(checkpoint), checkpoint)
            .AddInterceptors(interceptors).Options)
        { TenantContext = database };
    }

    private static ExploreDbContext Read(SourceDatabase database)
    {
        var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        return context;
    }

    private static async Task<EmbeddedPrivacyErasureAuthorityRepository> AuthorityAsync(
        ExploreDbContext primary, CommitCheckpoint checkpoint)
    {
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(primary.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
        await using (var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options))
        {
            // EnsureCreated would skip a database already containing application tables.
            // Use the authority's actual EF model, not hand-authored authority SQL.
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
        }
        var observed = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>(options)
            .AddInterceptors(new AuthorityCommit(checkpoint)).Options;
        return new(new AuthorityFactory(observed), new CommitClock(checkpoint.Database),
            Options.Create(new PrivacyErasureOptions()), applicationContext: primary);
    }

    private static ModerationQualificationRepository Writer(
        SourceDatabase database, ExploreDbContext context, EmbeddedPrivacyErasureAuthorityRepository authority)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, database.AdministratorId.ToString())], "content-commit-witness"))
            }
        };
        var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var clock = new CommitClock(database);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        return new(context, admin, machine, publications,
            new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock), clock, authority);
    }

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class CommitClock(SourceDatabase database) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => database.EvaluationTime;
    }

    private sealed class EnrollDisclosureDependency(CommitCheckpoint checkpoint) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var primary = (ExploreDbContext)eventData.Context!;
            if (checkpoint.ApprovalSaved
                || !primary.ChangeTracker.Entries<ModerationNoticeDisclosureApproval>().Any()) return result;
            checkpoint.ApprovalSaved = true;
            checkpoint.ApprovalTransaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            if (!primary.IdentityFenceOwnsTransaction
                || !ReferenceEquals(checkpoint.AuthorityTransaction, checkpoint.ApprovalTransaction))
                throw new InvalidOperationException("The real authority must own the adopted primary writer.");
            checkpoint.DisclosureBefore = (await primary.Set<EventDiscoveryRevision>().SingleAsync(cancellationToken))
                .DisclosureEpoch;
            // A real source write enrolls discovery through ExploreDbContext.CaptureAsync.
            // It remains unflushed until the authority owner performs its final flush.
            (await primary.Events.SingleAsync(cancellationToken)).Title = ChangedTitle;
            await primary.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    private sealed class CommitCheckpoint(SourceDatabase database, bool expireAtFlush) : DbCommandInterceptor
    {
        public SourceDatabase Database => database;
        public bool ApprovalSaved { get; set; }
        public long DisclosureBefore { get; set; }
        public int FlushCount { get; private set; }
        public DbTransaction? AuthorityTransaction { get; set; }
        public DbTransaction? ApprovalTransaction { get; set; }
        public List<DbTransaction> Committed { get; } = [];

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!ApprovalSaved || eventData.Context is not ExploreDbContext primary) return ValueTask.FromResult(result);
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var disclosure = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!
                .GetColumnName(table)!);
            // Observe the executed native epoch advancement, not its no-op row fence.
            var update = $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {disclosure} = ";
            if (command.Parameters.Count == 3 && command.CommandText.StartsWith(update, StringComparison.Ordinal))
            {
                if (result != 1 || !ReferenceEquals(command.Transaction, ApprovalTransaction)
                    || !primary.IdentityFenceOwnsTransaction || Committed.Count != 0)
                    throw new InvalidOperationException("Disclosure must flush in the still-owned adopted transaction.");
                FlushCount++;
                if (expireAtFlush) database.EvaluationTime = Now.AddMinutes(5);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AuthorityCommit(CommitCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not EmbeddedPrivacyErasureAuthorityDbContext
                || result.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Enrollment requires the native serializable authority writer.");
            checkpoint.AuthorityTransaction = result;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, checkpoint.AuthorityTransaction))
                throw new InvalidOperationException("Only the observed authority owner may commit.");
            checkpoint.Committed.Add(transaction);
            return Task.CompletedTask;
        }
    }
}
