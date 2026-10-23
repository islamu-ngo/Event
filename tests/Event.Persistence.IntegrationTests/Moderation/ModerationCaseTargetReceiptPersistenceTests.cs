using System.Data;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native receipt storage guards; execution is deferred to programme assurance.</summary>
public sealed class ModerationCaseTargetReceiptPersistenceTests
{
    [Test]
    [Arguments("caller")]
    [Arguments("token")]
    [Arguments("expiry")]
    [Arguments("epoch")]
    [Arguments("machine")]
    [Arguments("deleted")]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("anonymous")]
    public async Task NativeReceiptAuthenticationRejectsChangedSourceAfterQualifiedControl(string defect)
    {
        await using var database = await ReceiptDatabase.CreateAsync();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var caller = new Caller(database.CallerId);
        var machine = new Machine();
        var clock = new ReceiptClock();
        var repository = new ModerationCaseTargetReceiptRepository(context, caller, machine, clock);
        await using (var control = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var original = await repository.ReadForCurrentCallerAsync(database.ReceiptId, database.Token, default);
            await Assert.That(original?.Id).IsEqualTo(database.ReceiptId);
        }

        var token = (byte[])database.Token.Clone();
        switch (defect)
        {
            case "caller": caller.UserId = Guid.CreateVersion7(); break;
            case "token": token[0] ^= 1; break;
            case "expiry": clock.Now = ReceiptDatabase.Now.AddMinutes(5); break;
            case "epoch":
                await context.ModerationQualificationEpochs.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Revision, 2));
                break;
            case "machine":
                machine.Current = new ApiKeyPrincipalContext($"test-{Guid.CreateVersion7():N}",
                    database.TenantId, ExternalApiKeyOwnerType.User, database.CallerId, []);
                break;
            case "deleted":
                await context.Users.Where(row => row.Id == database.CallerId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            case "tenant":
                context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
                context.ModerationTrustDomainId = database.Instance.TrustDomainId;
                break;
            case "host":
                await context.ModerationHostBindings.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.InstanceId, Guid.CreateVersion7()));
                break;
            case "anonymous": caller.IsAuthenticated = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }

        await using (var rejected = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            await Assert.That(await repository.ReadForCurrentCallerAsync(database.ReceiptId, token, default)).IsNull();

        context.TenantContext = new SelectedTenant(database.TenantId);
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var retained = await context.ModerationCaseTargetReceipts.AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(1);
        await Assert.That(retained.ConsumedAt).IsNull();
        await Assert.That(retained.ConsumedCaseId).IsNull();
        await Assert.That(retained.ConsumedOperationId).IsNull();
    }

    [Test]
    public async Task ReceiptAuthenticationCannotEscapeItsSerializableWriterTransaction()
    {
        await using var database = await ReceiptDatabase.CreateAsync();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var repository = new ModerationCaseTargetReceiptRepository(
            context, new Caller(database.CallerId), new Machine(), new ReceiptClock());
        await Assert.That(async () => await repository.ReadForCurrentCallerAsync(
            database.ReceiptId, database.Token, default)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NativeReceiptReadsRequireBothBoundTrustAndOwningTenant()
    {
        await using var database = await ReceiptDatabase.CreateAsync();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationCaseTargetReceipts.CountAsync()).IsEqualTo(1);
        reader.ModerationTrustDomainId = null;
        await Assert.That(await reader.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        reader.ModerationTrustDomainId = Guid.CreateVersion7();
        await Assert.That(await reader.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        reader.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        reader.TenantContext = null;
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("qualification")]
    [Arguments("relationship")]
    [Arguments("target-kind")]
    [Arguments("target-tenant")]
    [Arguments("source-instance")]
    [Arguments("partial-consumption")]
    [Arguments("consumption-without-case")]
    public async Task NativeDatabaseRejectsForgedReceiptStructureWithoutChangingOriginal(string defect)
    {
        await using var database = await ReceiptDatabase.CreateAsync();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var receipts = context.ModerationCaseTargetReceipts;
        await Assert.That(async () =>
        {
            _ = defect switch
            {
                "expiry" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ExpiresAt, row => row.IssuedAt)),
                "qualification" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.QualificationRevision, 0)),
                "relationship" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.RelationshipRevision, 0)),
                "target-kind" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.TargetKind, (ModerationCaseTargetKind)0)),
                "target-tenant" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.TargetOriginTenantId, Guid.CreateVersion7())),
                "source-instance" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.SourceOriginInstanceId, Guid.CreateVersion7())),
                "partial-consumption" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ConsumedOperationId, Guid.CreateVersion7())),
                "consumption-without-case" => await receipts.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Revision, 2)
                        .SetProperty(row => row.ConsumedAt, ReceiptDatabase.Now)),
                _ => throw new ArgumentOutOfRangeException(nameof(defect))
            };
        }).Throws<SqliteException>();
        var retained = await receipts.AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(1);
        await Assert.That(retained.ConsumedAt).IsNull();
        await Assert.That(retained.ConsumedOperationId).IsNull();
        await Assert.That(retained.ExpiresAt).IsEqualTo(ReceiptDatabase.Now.AddMinutes(5));
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class Caller(Guid userId) : ICurrentUserService
    {
        public Guid? UserId { get; set; } = userId;
        public bool IsAuthenticated { get; set; } = true;
    }

    private sealed class Machine : IMachinePrincipalAccessor
    {
        public ApiKeyPrincipalContext? Current { get; set; }
        public bool IsMachineCaller => Current is not null;
    }

    private sealed class ReceiptClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ReceiptDatabase.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReceiptDatabase(string path) : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid CallerId { get; } = Guid.CreateVersion7();
        public Guid ReceiptId { get; } = Guid.CreateVersion7();
        public byte[] Token { get; } = RandomNumberGenerator.GetBytes(32);
        public AuthorityScope Instance { get; } = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());

        public ExploreDbContext Context() => new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseSnakeCaseNamingConvention().Options) { TenantContext = new SelectedTenant(TenantId) };

        public static async Task<ReceiptDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"moderation-intake-receipt-{Guid.CreateVersion7():N}.db");
            await LocalIdentitySqliteTemplate.CopyAsync(path, null, true, CancellationToken.None);
            var database = new ReceiptDatabase(path);
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            var tenant = new Tenant
            {
                Id = database.TenantId, Slug = $"intake-{database.TenantId:N}",
                FullName = "Intake receipt tests", TenantStatusId = status.Id, TenantStatus = status
            };
            var service = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(), Code = $"intake-{Guid.CreateVersion7():N}", DisplayName = "Maintenance"
            };
            var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), database.Instance.TrustDomainId,
                database.Instance.Parent!.Id, database.Instance.Id, service.Id);
            var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant);
            var origin = AuthorityScope.Tenant(database.Instance, tenant.Id);
            var evidence = ModerationEvidenceReference.Create(
                ModerationCaseTarget.Create(ModerationCaseTargetKind.EventVersion,
                    Guid.CreateVersion7(), origin, Guid.CreateVersion7()),
                origin, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            var receipt = ModerationCaseTargetReceipt.Issue(database.ReceiptId, origin,
                database.CallerId, host.Id, epoch.Id, epoch.Revision, Guid.CreateVersion7(),
                ModerationCaseRelationshipKind.EventRole, ModerationCaseRelationshipGeneration.FromRevision(1),
                Guid.CreateVersion7(), 1,
                evidence, SHA256.HashData(database.Token), Now, Now.AddMinutes(5));
            context.AddRange(tenant, service, host, epoch, receipt);
            context.Users.Add(new User
            {
                Id = database.CallerId, Pii = new UserPii
                {
                    UserId = database.CallerId, Email = $"{database.CallerId:N}@example.invalid",
                    FirstName = "Receipt", LastName = "Caller"
                }
            });
            await context.SaveChangesAsync();
            return database;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }
}
