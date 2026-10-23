using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Moderation;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNarrativeCustodyTests
{
    [Test]
    [Arguments("missing-policy")]
    [Arguments("substituted-material")]
    [Arguments("expired-policy")]
    [Arguments("missing-protector")]
    [Arguments("source-generation")]
    [Arguments("policy-receipt")]
    [Arguments("wrong-receipt")]
    public async Task FailedCustodyCannotPartiallyAcknowledgeOrConsume(string defect)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync(defect != "missing-policy");
        var issued = await fixture.IssueAsync("Original bounded allegation.");
        if (defect == "expired-policy") database.EvaluationTime = database.EvaluationTime.AddHours(3);
        await using var context = database.Context();
        if (defect == "source-generation")
            await context.ServicePrincipals.Where(row => row.Id == issued.Receipt.TargetId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
        if (defect == "policy-receipt")
        {
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await context.ModerationQualificationMutationReceipts
                .Where(row => row.SourceKind == ModerationQualificationSourceKind.CaseNarrativePolicySource)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RequestDigest, new string('0', 64)));
            context.ModerationTrustDomainId = null;
        }
        var request = fixture.Request(issued, defect == "substituted-material" ? "Substituted allegation." : "Original bounded allegation.");
        if (defect == "wrong-receipt") request = request with { ReceiptId = Guid.CreateVersion7() };
        var result = await fixture.Committer(context, defect == "missing-protector").SubmitAsync(request, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationCaseNarrative>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).ConsumedCaseId).IsNull();
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("owner-role")]
    [Arguments("caller")]
    public async Task FinalFlushFailureRollsBackNarrativeReceiptAndAcknowledgement(string defect)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync(retention: TimeSpan.FromMinutes(1));
        var issued = await fixture.IssueAsync("Exact material.");
        var checkpoint = new NarrativeFlush(async context =>
        {
            if (defect == "deadline") database.EvaluationTime = database.EvaluationTime.AddMinutes(1);
            if (defect == "owner-role")
                await context.PlatformUserRoles.Where(row => row.UserId == database.UrgencyAssessorId).ExecuteDeleteAsync();
            if (defect == "caller") fixture.ChangeCaller(database.SubjectId);
        });
        await using (var context = fixture.Context(checkpoint))
        {
            var result = await fixture.Committer(context).SubmitAsync(fixture.Request(issued, "Exact material."), default);
            await Assert.That(result.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        }
        await Assert.That(checkpoint.Observed).IsTrue();
        await using var verify = database.Context();
        verify.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await verify.Set<ModerationCaseNarrative>().AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync()).ConsumedCaseId).IsNull();
    }

    [Test]
    public async Task AmbiguousCommitVerifiesOnlyItsOwnOriginalAcceptance()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var issued = await fixture.IssueAsync("Exact material.");
        var checkpoint = new LostAcknowledgement();
        await using var context = fixture.Context(checkpoint);
        var request = fixture.Request(issued, "Exact material.");
        var first = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(first.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(checkpoint.Observed).IsTrue();
        var replay = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(replay.Acknowledgement!.Id).IsEqualTo(first.Acknowledgement!.Id);
        var substituted = await fixture.Committer(context).SubmitAsync(request with
        { Narrative = Encoding.UTF8.GetBytes("Different material.") }, default);
        await Assert.That(substituted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Conflict);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrative>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task LawfulErasurePurgesBeforeDeadlineWithoutPolicyOrProtectorAvailability()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var issued = await fixture.IssueAsync("Exact material.");
        await using var context = database.Context();
        await Assert.That((await fixture.Committer(context)
            .SubmitAsync(fixture.Request(issued, "Exact material."), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var instant = database.EvaluationTime.UtcDateTime;
        var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            database.AdministratorId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, instant, instant);
        context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1,
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), instant.AddHours(1), instant));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await context.PlatformUserRoles.Where(row => row.UserId == database.UrgencyAssessorId).ExecuteDeleteAsync();
        await Assert.That(await fixture.Store(context, unavailable: true)
            .PurgeForErasureAsync(database.AdministratorId, 100, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrative>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ExactReplayAfterPhysicalPurgeReturnsOnlyOriginalAcknowledgement()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var issued = await fixture.IssueAsync("A bounded allegation.");
        var request = fixture.Request(issued, "A bounded allegation.");
        await using var context = database.Context();
        var first = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(first.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        database.EvaluationTime = database.EvaluationTime.AddHours(1);
        await Assert.That(await fixture.Store(context).PurgeExpiredAsync(100, default)).IsEqualTo(1);
        var replay = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(replay.Acknowledgement!.Id).IsEqualTo(first.Acknowledgement!.Id);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrative>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task NewMaterialForOneOccurrenceNeverOverwritesOriginalNarrative()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var firstReceipt = await fixture.IssueAsync("First material fact.");
        var secondReceipt = await fixture.IssueAsync("Second material fact.");
        var firstRequest = fixture.Request(firstReceipt, "First material fact.");
        await using var context = database.Context();
        var first = await fixture.Committer(context).SubmitAsync(firstRequest, default);
        await Assert.That(first.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var second = await fixture.Committer(context).SubmitAsync(
            fixture.Request(secondReceipt, "Second material fact."), default);
        await Assert.That(second.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(second.Acknowledgement!.CaseId).IsEqualTo(first.Acknowledgement!.CaseId);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var rows = await context.Set<ModerationCaseNarrative>().ToListAsync();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Select(row => row.MaterialFactId).Distinct().Count()).IsEqualTo(2);
        await Assert.That(rows.All(row => !Encoding.UTF8.GetString(row.ProtectedMaterial.Span).Contains("material fact", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SubmissionReceiptDoesNotGrantNarrativeRead()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var issued = await fixture.IssueAsync("Protected allegation.");
        var request = fixture.Request(issued, "Protected allegation.");
        await using var context = database.Context();
        var accepted = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(await fixture.Store(context).ReadAsync(request with
        {
            Purpose = ModerationCaseAccessPurpose.ReadAllegation,
            OperationId = Guid.Empty,
            CaseId = accepted.Acknowledgement!.CaseId,
            ExpectedCaseRevision = accepted.Acknowledgement.CaseRevision,
            Narrative = default
        }, default)).IsNull();
    }

    [Test]
    [Arguments("privacy-role")]
    [Arguments("read-grant")]
    [Arguments("wrong-tenant")]
    [Arguments("wrong-caller")]
    [Arguments("ciphertext")]
    [Arguments("row-binding")]
    [Arguments("foreign-protector")]
    public async Task QualifiedReadRequiresCurrentPrivacyAndSeparateReadProof(string defect)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var fixture = new Custody(database);
        await fixture.PrepareAsync();
        var issued = await fixture.IssueAsync("Protected allegation.");
        var request = fixture.Request(issued, "Protected allegation.");
        await using var context = database.Context();
        var accepted = await fixture.Committer(context).SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        Guid grantId;
        await using (var grantContext = database.Context())
        {
            fixture.Identity(grantContext);
            await using var transaction = await grantContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = fixture.Owner(grantContext);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), accepted.Acknowledgement!.CaseId,
                accepted.Acknowledgement.CaseRevision, issued.Receipt.SourceId,
                database.AdministratorId, database.EvaluationTime.AddMinutes(5)), default);
            await Assert.That(grant).IsNotNull();
            await grantContext.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            grantId = grant!.Id;
        }
        var readReceipt = await fixture.Committer(context).IssueReadReceiptAsync(grantId, default);
        await Assert.That(readReceipt).IsNotNull();
        var read = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.ReadAllegation, accepted.Acknowledgement!.CaseId,
            Guid.Empty, accepted.Acknowledgement!.CaseRevision, readReceipt!.Receipt.Id, readReceipt.Token);
        using (var disclosed = await fixture.Store(context).ReadAsync(read, default))
        {
            await Assert.That(disclosed).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(disclosed!.Material.Span)).IsEqualTo("Protected allegation.");
            await Assert.That(disclosed.ToString()).DoesNotContain("Protected allegation.");
        }
        if (defect == "privacy-role")
            await context.PlatformUserRoles.Where(row => row.UserId == database.UrgencyAssessorId).ExecuteDeleteAsync();
        if (defect == "read-grant")
        {
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.RevokedAt, database.EvaluationTime).SetProperty(row => row.Revision, 2));
            context.ModerationTrustDomainId = null;
        }
        if (defect is "ciphertext" or "row-binding")
        {
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var original = await context.ModerationCaseNarratives.AsNoTracking().SingleAsync();
            if (defect == "ciphertext")
            {
                var corrupted = original.ProtectedMaterial.ToArray();
                try
                {
                    corrupted[^1] ^= 1;
                    await context.ModerationCaseNarratives.Where(row => row.Id == original.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            row => EF.Property<byte[]>(row, "protectedMaterial"), corrupted));
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(corrupted); }
            }
            else
                await context.ModerationCaseNarratives.Where(row => row.Id == original.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PurgeAt,
                        original.PurgeAt.AddTicks(TimeSpan.TicksPerMicrosecond)));
            context.ModerationTrustDomainId = null;
        }
        var store = fixture.Store(context, foreignProtector: defect == "foreign-protector");
        if (defect == "wrong-caller") fixture.ChangeCaller(database.SubjectId);
        if (defect == "wrong-tenant") context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        await Assert.That(await store.ReadAsync(read, default)).IsNull();
        var released = false;
        await Assert.That(await store.DiscloseAsync(read, (_, _) =>
        {
            released = true;
            return Task.CompletedTask;
        }, default)).IsFalse();
        await Assert.That(released).IsFalse();
    }

    private sealed class Custody(SourceDatabase database, Guid? selectedTenant = null, Guid? reporter = null)
    {
        public Guid ReporterId => reporter ?? database.AdministratorId;
        public AuthorityScope Scope => AuthorityScope.Tenant(database.Instance, selectedTenant ?? database.TenantId);
        private readonly IDataProtectionProvider keys = new EphemeralDataProtectionProvider();
        private readonly NarrativeClock clock = new(database);
        private readonly HttpContextAccessor http = new()
        {
            HttpContext = new DefaultHttpContext()
        };

        public void ChangeCaller(Guid id) => http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "narrative-witness"));

        public ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            using var original = database.Context();
            var options = (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>();
            return new(new DbContextOptionsBuilder<ExploreDbContext>(options).AddInterceptors(interceptors)
                .ReplaceService<IExecutionStrategyFactory, NarrativeRetryingStrategyFactory>().Options)
            { TenantContext = new SelectedTenant(Scope.Id) };
        }

        public void Identity(ExploreDbContext context)
        {
            context.TenantContext = new SelectedTenant(Scope.Id);
            http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ReporterId.ToString())], "narrative-witness"));
            context.CurrentUserService = new NarrativeCaller(http);
        }

        public AdminContext Admin(ExploreDbContext context) => new(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);

        public MachinePrincipalAccessor Machine => new(http);

        public ModerationCaseTargetProofOwner Owner(ExploreDbContext context) =>
            new(context, new NarrativeCaller(http), new MachinePrincipalAccessor(http), clock);

        public ModerationCaseNarrativeRepository Store(ExploreDbContext context, bool unavailable = false,
            bool foreignProtector = false)
        {
            Identity(context);
            var admin = Admin(context);
            var machine = new MachinePrincipalAccessor(http);
            var intake = new ModerationCaseIntakeRepository(context,
                new ModerationCaseTargetReceiptRepository(context, new NarrativeCaller(http), machine, clock), Owner(context), clock);
            return new(context, admin, new NarrativeCaller(http), machine, intake, clock,
                unavailable ? null : foreignProtector ? new EphemeralDataProtectionProvider() : keys);
        }

        public ModerationCaseIntakeCommitter Committer(ExploreDbContext context, bool unavailable = false)
        {
            Identity(context);
            var caller = new NarrativeCaller(http);
            var machine = new MachinePrincipalAccessor(http);
            var intake = new ModerationCaseIntakeRepository(context,
                new ModerationCaseTargetReceiptRepository(context, caller, machine, clock), Owner(context), clock);
            return new(context, intake, Owner(context), caller, machine, clock, Store(context, unavailable), Admin(context));
        }

        public async Task PrepareAsync(bool policy = true, TimeSpan? retention = null)
        {
            if (policy)
            {
                var source = ModerationCaseNarrativeQualificationTests.Request(database) with { Target = Scope };
                if (retention is { } ceiling) source = source with { Terms = source.Terms with { MaximumRetention = ceiling } };
                await Assert.That((await QualifyAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
                await Assert.That((await QualifyAsync(database.UrgencyAssessorId,
                    new AdoptModerationCaseNarrativePolicySource
                    {
                        OperationId = Guid.CreateVersion7(),
                        Target = Scope,
                        ExpectedQualificationRevision = 2,
                        SourceId = source.SourceId,
                        ExpectedSourceRevision = 1,
                        ExpectedSourceWriterGeneration = 2,
                        AdoptionId = Guid.CreateVersion7(),
                        ValidUntil = source.ValidUntil
                    })).IsSuccess).IsTrue();
            }
            var budget = new RecordModerationOperationalBudgetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Scope,
                ExpectedQualificationRevision = policy ? 3 : 1,
                SourceId = Guid.CreateVersion7(),
                OperatorHumanId = database.UrgencyAssessorId,
                Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 8, TimeSpan.FromHours(1)),
                ValidUntil = database.EvaluationTime.AddHours(2)
            };
            await Assert.That((await QualifyAsync(database.AdministratorId, budget)).IsSuccess).IsTrue();
            await Assert.That((await QualifyAsync(database.UrgencyAssessorId, new AdoptModerationOperationalBudgetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Scope,
                ExpectedQualificationRevision = policy ? 4 : 2,
                SourceId = budget.SourceId,
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = policy ? 4 : 2,
                AdoptionId = Guid.CreateVersion7(),
                ValidUntil = budget.ValidUntil
            })).IsSuccess).IsTrue();
            await using var context = database.Context();
            Identity(context);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = Owner(context);
            await Assert.That(await owner.SetLifetimePolicyAsync(ModerationCaseTargetKind.Service,
                0, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), true, default)).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }

        public async Task<ModerationQualificationResponse> QualifyAsync(Guid actor, ModerationQualificationMutation mutation)
        {
            await using var context = Context();
            Identity(context);
            ChangeCaller(actor);
            var admin = Admin(context);
            var publications = new ModerationPolicyPublicationRepository(context, admin, Machine, database.Inventory, null, clock);
            var activation = new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock);
            return await new ApplyModerationQualificationCommandHandler(
                new ModerationQualificationRepository(context, admin, Machine, publications, activation, clock, database.Privacy.Authority(context, clock)))
                .ExecuteAsync(new(mutation));
        }

        public async Task<ModerationCaseIssuedReceipt> IssueAsync(string material)
        {
            await using var context = database.Context();
            Identity(context);
            var host = await context.ModerationHostBindings.SingleAsync();
            var grant = await context.PlatformUserRoles.SingleAsync(row => row.UserId == ReporterId);
            context.ChangeTracker.Clear();
            var receipt = await Committer(context).IssueSubmissionReceiptAsync(new(
                ModerationCaseTargetKind.Service, ModerationCaseTargetKind.Service, null, host.MaintenanceServicePrincipalId,
                ModerationCaseRelationshipKind.InstanceAdministrator, grant.Id,
                Encoding.UTF8.GetBytes("One occurrence."), Encoding.UTF8.GetBytes(material)), default);
            await Assert.That(receipt).IsNotNull();
            return receipt!;
        }

        public ModerationCaseAccessRequest Request(ModerationCaseIssuedReceipt receipt, string material) =>
            new(ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                receipt.Receipt.Id, receipt.Token)
            { Narrative = Encoding.UTF8.GetBytes(material) };
    }

    private sealed class NarrativeClock(SourceDatabase database) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => database.EvaluationTime;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private sealed class NarrativeFlush(Func<ExploreDbContext, Task> afterSave) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (!Observed && context.ChangeTracker.Entries<ModerationCaseNarrative>().Any())
            {
                Observed = true;
                await afterSave(context);
            }
            return result;
        }
    }

    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        public bool Observed { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Observed)
            {
                Observed = true;
                throw new SqliteException("Injected lost native commit acknowledgement.", 5);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class NarrativeRetryingStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new NarrativeRetryingStrategy(dependencies);
    }

    private sealed class NarrativeRetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException;
    }

    private sealed class NarrativeCaller(IHttpContextAccessor http) : ICurrentUserService
    {
        public Guid? UserId => Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public bool IsAuthenticated => http.HttpContext?.User.Identity?.IsAuthenticated == true;
    }
}
