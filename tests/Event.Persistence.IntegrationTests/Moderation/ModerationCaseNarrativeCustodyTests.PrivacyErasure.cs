using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.Privacy;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNarrativeCustodyTests
{
    [Test]
    [Arguments(1, false)]
    [Arguments(101, true)]
    public async Task OrdinaryErasureSettlesEveryOriginalReporterTenantWithoutAdoption(
        int firstTenantRows, bool withdrawCustody)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (first, second, original) = await PrepareErasureNarrativesAsync(database, firstTenantRows);
        if (withdrawCustody)
        {
            foreach (var custody in new[] { first, second })
            {
                await using var context = custody.Context();
                context.ModerationTrustDomainId = database.Instance.TrustDomainId;
                var adoption = await context.ModerationCaseNarrativeAdoptions.SingleAsync();
                await Assert.That((await custody.QualifyAsync(database.AdministratorId,
                    new WithdrawModerationCaseNarrativeAdoption
                    {
                        OperationId = Guid.CreateVersion7(),
                        Target = custody.Scope,
                        ExpectedQualificationRevision = 3,
                        AdoptionId = adoption.Id,
                        ExpectedAdoptionRevision = 1
                    })).IsSuccess).IsTrue();
            }
            await using var retiredTenant = second.Context();
            await retiredTenant.Tenants.Where(row => row.Id == second.Scope.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.TenantStatusId, (int)TenantStatusEnum.Archived));
        }
        var clock = new NarrativeClock(database);
        await using var authority = await NarrativeErasureAuthority.CreateAsync(clock);
        var commands = new NarrativeErasureCommands();
        Guid intentId = Guid.CreateVersion7();
        await using (var context = first.Context(commands))
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            context, authority.Repository(context), clock))
        {
            if (withdrawCustody) context.TenantContext = null;
            var result = await runtime.Service.EraseUserAsync(database.AdministratorId, intentId, default);
            await Assert.That(result.Status).IsEqualTo("completed");
        }
        await Assert.That(commands.PayloadSelected).IsFalse();
        await Assert.That(commands.MaximumDeletedRows).IsLessThanOrEqualTo(100);
        await Assert.That(commands.DeletedRows).IsEqualTo(firstTenantRows + 1);
        await AssertNarrativeErasureAsync(database, firstTenantRows + 3, intentId);

        // Fresh runtime replay and the original native intake request cannot restore bytes.
        await using (var context = second.Context())
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            context, authority.Repository(context), clock))
            await runtime.Service.ReplayPendingAsync(default);
        await using (var context = first.Context())
        {
            var replay = await first.Committer(context).SubmitAsync(original, default);
            await Assert.That(replay.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        }
        await AssertNarrativeErasureAsync(database, firstTenantRows + 3, intentId);
    }

    [Test]
    public async Task FailedNarrativeBatchRollsBackLocalSettlementAndNativeReplayFinishes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (first, _, _) = await PrepareErasureNarrativesAsync(database, 101);
        var clock = new NarrativeClock(database);
        await using var authority = await NarrativeErasureAuthority.CreateAsync(clock);
        var commands = new NarrativeErasureCommands(failAfterFirstDelete: true);
        Guid intentId = Guid.CreateVersion7();
        await using (var context = first.Context(commands))
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            context, authority.Repository(context), clock))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runtime.Service.EraseUserAsync(database.AdministratorId, intentId, default));
        await Assert.That(commands.DeletedRows).IsGreaterThan(0);
        await Assert.That(commands.MaximumDeletedRows).IsLessThanOrEqualTo(100);
        await Assert.That(commands.PayloadSelected).IsFalse();
        await using (var context = first.Context())
        {
            await Assert.That(await context.ModerationCaseNarratives.IgnoreQueryFilters().CountAsync()).IsEqualTo(104);
            var saga = await context.PrivacyErasureSagas.SingleAsync(row => row.IntentId == intentId);
            await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.Fenced);
            await Assert.That(saga.LocalSettledAtUtc).IsNull();
            await Assert.That(await context.PrivacyErasurePolicyCoverage.AnyAsync()).IsFalse();
            await Assert.That(await context.PrivacyErasureReplayCheckpoints.AnyAsync()).IsFalse();
            await Assert.That(await context.Users.AnyAsync(row => row.Id == database.AdministratorId && !row.IsDeleted)).IsTrue();
        }
        await using (var context = first.Context())
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            context, authority.Repository(context), clock))
            await runtime.Service.ReplayPendingAsync(default);
        await AssertNarrativeErasureAsync(database, 104, intentId);
        await using var verify = first.Context();
        await Assert.That(await verify.PrivacyErasurePolicyCoverage.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.PrivacyErasureReplayCheckpoints.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task OrdinaryErasureWithoutAnyNarrativeOrRecognitionPolicyCompletes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var clock = new NarrativeClock(database);
        await using var authority = await NarrativeErasureAuthority.CreateAsync(clock);
        await using var context = database.Context();
        await using var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            context, authority.Repository(context), clock);
        var result = await runtime.Service.EraseUserAsync(database.AdministratorId, Guid.CreateVersion7(), default);
        await Assert.That(result.Status).IsEqualTo("completed");
        await Assert.That(await context.ModerationCaseNarrativePolicySources.IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationRecognitionPrivacySource>().IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationRecognitionPending>().IgnoreQueryFilters().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("no-transaction")]
    [Arguments("no-saga")]
    [Arguments("foreign-intent")]
    public async Task NarrativeDispositionRequiresTheExactNativeSagaInsideSettlement(string defect)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var custody = new Custody(database);
        await custody.PrepareAsync();
        var issued = await custody.IssueAsync("Confidential authority witness.");
        await using var context = custody.Context();
        await Assert.That((await custody.Committer(context)
            .SubmitAsync(custody.Request(issued, "Confidential authority witness."), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var now = database.EvaluationTime.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            database.AdministratorId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        if (defect != "no-saga")
        {
            var saved = defect == "foreign-intent"
                ? PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                    database.ReviewerId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now)
                : fact;
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(saved, saved.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
        await using var transaction = defect == "no-transaction" ? null
            : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default));
        await Assert.That(await context.ModerationCaseNarratives.IgnoreQueryFilters().CountAsync()).IsEqualTo(1);
    }

    private static async Task<(Custody First, Custody Second, ModerationCaseAccessRequest Original)>
        PrepareErasureNarrativesAsync(SourceDatabase database, int firstTenantRows)
    {
        var first = new Custody(database);
        await first.PrepareAsync();
        Guid tenantId = Guid.CreateVersion7();
        var second = new Custody(database, tenantId);
        await using (var context = second.Context())
        {
            second.Identity(context);
            int status = await context.TenantStatuses.Where(row => row.IsActiveState).Select(row => row.Id).FirstAsync();
            // Cross a tenant-inventory page as well as a narrative page. Membership is
            // deliberately absent in the second reporter tenant and is not erasure authority.
            foreach (var id in Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).Append(tenantId))
                context.Tenants.Add(new Tenant
                {
                    Id = id,
                    Slug = $"erasure-{id:N}",
                    FullName = "Narrative erasure tenant",
                    TenantStatusId = status,
                    TenantStatus = null!
                });
            await context.SaveChangesAsync();
            var host = await context.ModerationHostBindings.AsNoTracking().SingleAsync();
            context.ChangeTracker.Clear();
            var established = await new EstablishModerationAuthoritySourcesCommandHandler(
                new ModerationAuthoritySourceRepository(context, second.Admin(context), second.Machine))
                .ExecuteAsync(new EstablishModerationAuthoritySourcesCommand
                {
                    HostBindingId = host.Id,
                    TrustDomainId = host.TrustDomainId,
                    SolutionId = host.SolutionId,
                    InstanceId = host.InstanceId,
                    MaintenanceServicePrincipalId = host.MaintenanceServicePrincipalId,
                    TenantId = tenantId,
                    EpochId = Guid.CreateVersion7()
                });
            await Assert.That(established.IsSuccess).IsTrue();
        }
        await second.PrepareAsync();
        ModerationCaseAccessRequest? original = null;
        foreach (var (custody, count) in new[] { (first, firstTenantRows), (second, 1),
                     (new Custody(database, first.Scope.Id, database.ReviewerId), 1),
                     (new Custody(database, second.Scope.Id, database.ReviewerId), 1) })
        {
            for (int index = 0; index < count; index++)
            {
                string material = $"Confidential erasure canary {custody.ReporterId:N}/{index}.";
                var receipt = await custody.IssueAsync(material);
                var request = custody.Request(receipt, material);
                await using var context = custody.Context();
                var accepted = await custody.Committer(context).SubmitAsync(request, default);
                await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
                original ??= request;
            }
        }
        return (first, second, original!);
    }

    private static async Task AssertNarrativeErasureAsync(SourceDatabase database, int acknowledgements, Guid intentId)
    {
        await using var context = database.Context();
        var survivors = await context.ModerationCaseNarratives.IgnoreQueryFilters()
            .Select(row => new { row.ReporterUserId, row.TenantId }).ToArrayAsync();
        await Assert.That(survivors.Length).IsEqualTo(2);
        await Assert.That(survivors.All(row => row.ReporterUserId == database.ReviewerId)).IsTrue();
        await Assert.That(survivors.Select(row => row.TenantId).Distinct().Count()).IsEqualTo(2);
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.IgnoreQueryFilters().CountAsync())
            .IsEqualTo(acknowledgements);
        var saga = await context.PrivacyErasureSagas.SingleAsync(row => row.IntentId == intentId);
        await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.Completed);
        await Assert.That(await context.Set<ModerationRecognitionPrivacySource>().IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationRecognitionPending>().IgnoreQueryFilters().AnyAsync()).IsFalse();
        var payloads = await context.OutboxMessages.Select(row => row.Payload).ToArrayAsync();
        await Assert.That(payloads.All(value => value is null
            || !value.Contains("Confidential erasure canary", StringComparison.Ordinal))).IsTrue();
    }

    private sealed class NarrativeErasureCommands(bool failAfterFirstDelete = false) : DbCommandInterceptor
    {
        public bool PayloadSelected { get; private set; }
        public int MaximumDeletedRows { get; private set; }
        public int DeletedRows { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("protected_material", StringComparison.OrdinalIgnoreCase))
                PayloadSelected = true;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"moderation_case_narratives\"", StringComparison.Ordinal))
            {
                MaximumDeletedRows = Math.Max(MaximumDeletedRows, result);
                DeletedRows += result;
                if (failAfterFirstDelete) throw new InvalidOperationException("injected_narrative_erasure_failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NarrativeErasureAuthority(
        SqliteConnection connection, DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options, TimeProvider clock)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>, IAsyncDisposable
    {
        public static async Task<NarrativeErasureAuthority> CreateAsync(TimeProvider clock)
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(connection.ConnectionString).UseSnakeCaseNamingConvention().Options;
            var result = new NarrativeErasureAuthority(connection, options, clock);
            await using var context = result.CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            return result;
        }

        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
        public EmbeddedPrivacyErasureAuthorityRepository Repository(ExploreDbContext context) =>
            new(this, clock, Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
