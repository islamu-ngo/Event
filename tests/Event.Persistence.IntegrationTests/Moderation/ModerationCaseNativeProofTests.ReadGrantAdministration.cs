using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task ReadGrantCreationCommitsExactOperationAndRejectsChangedRetry()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, originalId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var original = await context.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == originalId);
        var request = new ModerationCaseReadGrantRequest(Guid.CreateVersion7(), caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        var repository = ReadGrantRepository(context, database);
        var created = await repository.GrantAsync(request, default);
        await Assert.That(created).IsNotNull();
        await Assert.That(created!.Id).IsEqualTo(request.OperationId);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        var replay = await repository.GrantAsync(request, default);
        await Assert.That(replay!.Id).IsEqualTo(created.Id);
        await Assert.That(replay.IssuedAt).IsEqualTo(created.IssuedAt);
        await Assert.That(await repository.GrantAsync(request with { ValidUntil = request.ValidUntil.AddSeconds(1) }, default)).IsNull();
        await Assert.That(await repository.GrantAsync(request with { ExpectedCaseRevision = request.ExpectedCaseRevision + 1 }, default)).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.CountAsync(row => row.Id == request.OperationId)).IsEqualTo(1);
    }

    [Test]
    public async Task ReadGrantRevocationBindsExactOperationAndCannotRestoreReadAuthority()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var repository = ReadGrantRepository(context, database);
        var operationId = Guid.CreateVersion7();
        var revoked = await repository.RevokeAsync(grantId, operationId, 1, default);
        await Assert.That(revoked).IsNotNull();
        await Assert.That(revoked!.Revision).IsEqualTo(2);
        var replay = await repository.RevokeAsync(grantId, operationId, 1, default);
        await Assert.That(replay!.RevokedAt).IsEqualTo(revoked.RevokedAt);
        await Assert.That(await repository.RevokeAsync(grantId, Guid.CreateVersion7(), 1, default)).IsNull();
        await Assert.That(await repository.RevokeAsync(grantId, operationId, 2, default)).IsNull();
        await Assert.That(await IssuanceCommitter(context, database, database.Caller)
            .IssueReadReceiptAsync(grantId, default)).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrantRevocationReceipts.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ReadGrantRepositoryPreservesForeignTrackedWorkAndCallerTransaction()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var grant = await context.ModerationCaseReadGrants.SingleAsync(row => row.Id == grantId);
        var repository = ReadGrantRepository(context, database);
        await Assert.That(await repository.RevokeAsync(grantId, Guid.CreateVersion7(), 1, default)).IsNull();
        await Assert.That(context.Entry(grant).State).IsEqualTo(EntityState.Unchanged);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await Assert.That(await repository.RevokeAsync(grantId, Guid.CreateVersion7(), 1, default)).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
    }

    [Test]
    [Arguments("case")]
    [Arguments("source")]
    [Arguments("native")]
    [Arguments("administrator")]
    [Arguments("account")]
    [Arguments("policy")]
    [Arguments("expiry")]
    public async Task ReadGrantCreationRollsBackIfNativeAuthorityChangesAfterFlush(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        ModerationCaseReadGrant original;
        await using (var inspect = database.Context())
            original = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var request = new ModerationCaseReadGrantRequest(Guid.CreateVersion7(), caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        var checkpoint = new ReadGrantSavedCheckpoint(async context =>
        {
            switch (defect)
            {
                case "case":
                    await context.ModerationCases.Where(row => row.Id == caseId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1));
                    break;
                case "source":
                    await context.ModerationCaseTargetSources.Where(row => row.Id == original.SourceId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Version, Guid.CreateVersion7()));
                    break;
                case "native":
                    await context.Events.Where(row => row.Id == database.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                    break;
                case "administrator":
                    await context.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteDeleteAsync();
                    break;
                case "account":
                    await context.Users.Where(row => row.Id == database.CallerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                    break;
                case "policy":
                    await context.ModerationCaseTargetPolicies
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Enabled, false));
                    break;
                case "expiry": database.Clock.Now = request.ValidUntil; break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        });
        await using var context = database.Context(checkpoint);
        await Assert.That(await ReadGrantRepository(context, database).GrantAsync(request, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostGrantCommitAcknowledgementDoesNotCreateAnotherGrantOnRetry(bool revoke)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        ModerationCaseReadGrant original;
        await using (var inspect = database.Context())
            original = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var operationId = Guid.CreateVersion7();
        var request = new ModerationCaseReadGrantRequest(operationId, caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        await using (var failed = database.RetryingContext(new ReceiptCommitFailureCheckpoint(true)))
        {
            var repository = ReadGrantRepository(failed, database);
            await Assert.That(revoke
                ? await repository.RevokeAsync(grantId, operationId, 1, default)
                : await repository.GrantAsync(request, default)).IsNull();
        }
        await using var retry = database.Context();
        var writer = ReadGrantRepository(retry, database);
        var result = revoke ? await writer.RevokeAsync(grantId, operationId, 1, default)
            : await writer.GrantAsync(request, default);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(revoke ? grantId : operationId);
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.CountAsync()).IsEqualTo(revoke ? 1 : 2);
        await Assert.That(await verify.ModerationCaseReadGrantRevocationReceipts.CountAsync()).IsEqualTo(revoke ? 1 : 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeRetryCannotUseAReplacementRoleGrant(bool revoke)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var original = await context.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var operationId = Guid.CreateVersion7();
        var repository = ReadGrantRepository(context, database);
        if (revoke)
            await Assert.That(await repository.RevokeAsync(grantId, operationId, 1, default)).IsNotNull();
        await using (var change = database.Context())
        {
            await change.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteDeleteAsync();
            change.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = database.CallerId, User = null!, Role = null!,
                RoleId = (int)Explore.Domain.Enums.RoleEnum.Admin, GrantedAt = database.Clock.Now.UtcDateTime
            });
            await change.SaveChangesAsync();
        }
        await Assert.That(revoke ? await repository.RevokeAsync(grantId, operationId, 1, default)
            : await repository.GrantAsync(new(original.Id, original.CaseId, original.CaseRevision,
                original.SourceId, original.CallerUserId, original.ExpiresAt), default)).IsNull();
    }

    [Test]
    public async Task ExpiredGrantRetryCannotExtendItsWindowOrProduceReadReceipt()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var original = await context.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        database.Clock.Now = original.ExpiresAt;
        var repository = ReadGrantRepository(context, database);
        var request = new ModerationCaseReadGrantRequest(original.Id, original.CaseId, original.CaseRevision,
            original.SourceId, original.CallerUserId, original.ExpiresAt);
        await Assert.That(await repository.GrantAsync(request, default)).IsNull();
        await Assert.That(await repository.GrantAsync(request with { ValidUntil = database.Clock.Now.AddSeconds(30) }, default)).IsNull();
        await Assert.That(await IssuanceCommitter(context, database, database.Caller).IssueReadReceiptAsync(grantId, default)).IsNull();
    }

    [Test]
    [Arguments("administrator")]
    [Arguments("receipt")]
    [Arguments("grant")]
    [Arguments("foreign-write")]
    public async Task RevocationRollsBackWhenFinalAuthorityOrOwnedWriteGuardFails(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        var operationId = Guid.CreateVersion7();
        var checkpoint = new ReadGrantSavedCheckpoint(async context =>
        {
            switch (defect)
            {
                case "administrator":
                    await context.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteDeleteAsync();
                    break;
                case "receipt":
                    await context.ModerationCaseReadGrantRevocationReceipts.Where(row => row.Id == operationId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, database.Clock.Now.AddSeconds(1)));
                    break;
                case "grant":
                    await context.ModerationCaseReadGrants.Where(row => row.Id == grantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, database.Clock.Now.AddHours(1)));
                    break;
                case "foreign-write":
                    context.Users.Add(new User { Id = Guid.CreateVersion7(), Pii = null! });
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        });
        await using var context = database.Context(checkpoint);
        await Assert.That(await ReadGrantRepository(context, database).RevokeAsync(grantId, operationId, 1, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That((await verify.ModerationCaseReadGrants.SingleAsync(row => row.Id == grantId)).RevokedAt).IsNull();
        await Assert.That(await verify.ModerationCaseReadGrantRevocationReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("expired")]
    [Arguments("unbounded")]
    [Arguments("policy-window")]
    [Arguments("grantee")]
    [Arguments("case")]
    [Arguments("source")]
    [Arguments("tenant")]
    public async Task ReadGrantRequiresExactNativeCaseMaterialAccountAndFiniteWindow(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var original = await context.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var request = new ModerationCaseReadGrantRequest(Guid.CreateVersion7(), caseId, original.CaseRevision,
            original.SourceId, database.CallerId, database.Clock.Now.AddSeconds(30));
        request = defect switch
        {
            "expired" => request with { ValidUntil = database.Clock.Now },
            "unbounded" => request with { ValidUntil = DateTimeOffset.MaxValue },
            "policy-window" => request with { ValidUntil = database.Clock.Now.AddDays(2) },
            "grantee" => request with { CallerUserId = Guid.CreateVersion7() },
            "case" => request with { CaseId = Guid.CreateVersion7() },
            "source" => request with { SourceId = Guid.CreateVersion7() },
            "tenant" => request,
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        if (defect == "tenant") context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        await Assert.That(await ReadGrantRepository(context, database).GrantAsync(request, default)).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseReadGrants.CountAsync()).IsEqualTo(1);
    }

    private sealed class ReadGrantSavedCheckpoint(Func<Explore.Persistence.ExploreDbContext, Task> mutate) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is Explore.Persistence.ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationCaseReadGrant>().Any())
            {
                Observed = true;
                await mutate(context);
            }
            return result;
        }
    }

    private static ModerationCaseReadGrantRepository ReadGrantRepository(
        Explore.Persistence.ExploreDbContext context, NativeDatabase database)
    {
        context.ModerationTrustDomainId = null;
        return new(context, database.Owner(context), database.Caller, database.Machine);
    }
}
