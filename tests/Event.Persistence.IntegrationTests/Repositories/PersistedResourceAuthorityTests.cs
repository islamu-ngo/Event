using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Policies;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PersistedResourceAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TrackedForeignEntityCannotSatisfyGetById()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        context.Attach(rows.Foreign);

        var result = await new GenericRepository<TenantSetting, Guid>(context).GetById(rows.Foreign.Id);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TrackedValueDoesNotReplaceThePersistedRead()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        rows.Current.Value = "uncommitted";
        context.Attach(rows.Current);

        var result = await new GenericRepository<TenantSetting, Guid>(context).GetById(rows.Current.Id);

        await Assert.That(result!.Value).IsEqualTo("original");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForeignTrackedMutationIsRejectedBeforePersistence(bool delete)
    {
        var rows = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            context.Attach(rows.Foreign);
            rows.Foreign.Value = "forbidden";
            var repository = new GenericRepository<TenantSetting, Guid>(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                delete ? repository.Delete(rows.Foreign) : repository.Update(rows.Foreign));
        }

        await using var verification = fixture.CreateDbContext();
        var retained = await verification.Set<TenantSetting>().SingleAsync(row => row.Id == rows.Foreign.Id);
        await Assert.That(retained.Value).IsEqualTo("original");
        await Assert.That(retained.TenantId).IsEqualTo(rows.ForeignTenantId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForgedAttachedTenantCannotReplacePersistedOwnership(bool delete)
    {
        var rows = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            rows.Foreign.TenantId = rows.CurrentTenantId;
            rows.Foreign.Value = "forbidden";
            context.Attach(rows.Foreign);
            context.Entry(rows.Foreign).State = delete ? EntityState.Deleted : EntityState.Modified;

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using var verification = fixture.CreateDbContext();
        var retained = await verification.Set<TenantSetting>().SingleAsync(row => row.Id == rows.Foreign.Id);
        await Assert.That(retained.Value).IsEqualTo("original");
        await Assert.That(retained.TenantId).IsEqualTo(rows.ForeignTenantId);
    }

    [Test]
    public async Task OwnedNativeTransactionRollsBackEarlierWriteWhenForeignWriteIsRejected()
    {
        var rows = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            var unitOfWork = new EfCoreUnitOfWork(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    rows.Current.Value = "must roll back";
                    await new GenericRepository<TenantSetting, Guid>(context).Update(rows.Current);
                    rows.Foreign.TenantId = rows.CurrentTenantId;
                    context.Attach(rows.Foreign);
                    context.Entry(rows.Foreign).State = EntityState.Deleted;
                    await context.SaveChangesAsync(token);
                    return true;
                }));
        }

        await using var verification = fixture.CreateDbContext();
        await Assert.That((await verification.Set<TenantSetting>().SingleAsync(
            row => row.Id == rows.Current.Id)).Value).IsEqualTo("original");
        await Assert.That(await verification.Set<TenantSetting>().AnyAsync(
            row => row.Id == rows.Foreign.Id)).IsTrue();
    }

    [Test]
    public async Task SameTenantMutationStillPersists()
    {
        var rows = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            rows.Current.Value = "updated";
            await new GenericRepository<TenantSetting, Guid>(context).Update(rows.Current);
        }

        await using var verification = fixture.CreateDbContext();
        await Assert.That((await verification.Set<TenantSetting>().SingleAsync(
            row => row.Id == rows.Current.Id)).Value).IsEqualTo("updated");
    }

    [Test]
    public async Task CancellationAfterNativeSaveRollsBackTheOwnedTransaction()
    {
        var rows = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new EfCoreUnitOfWork(context)
                .ExecuteSerializableAsync(async token =>
                {
                    rows.Current.Value = "must roll back";
                    await new GenericRepository<TenantSetting, Guid>(context).Update(rows.Current);
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return true;
                }, cancellation.Token));
        }
        await using var verification = fixture.CreateDbContext();
        await Assert.That((await verification.Set<TenantSetting>().SingleAsync(
            row => row.Id == rows.Current.Id)).Value).IsEqualTo("original");
    }

    [Test]
    public async Task ParentDerivedTenantCannotBeBypassedByAttachingOnlyTheChild()
    {
        var rows = await SeedAsync();
        Guid versionId;
        await using (var seed = fixture.CreateDbContext())
        {
            var document = LegalDocument.CreateDraft(LegalDocumentScope.Tenant, rows.ForeignTenantId,
                LegalDocumentKind.TenantTerms, LegalDocumentAudience.Public,
                [LegalDocumentLocalizedSource.Create("en", "Policy", "Summary", "Original policy")],
                null, "tenant-identity", false, DateTime.UtcNow);
            seed.LegalDocuments.Add(document);
            await seed.SaveChangesAsync();
            versionId = document.Versions.Single().Id;
        }
        LegalDocumentVersion child;
        await using (var source = fixture.CreateDbContext())
            child = await source.Set<LegalDocumentVersion>().AsNoTracking().SingleAsync(row => row.Id == versionId);

        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            context.Attach(child);
            context.Entry(child).Property(row => row.ReviewEvidenceReference).CurrentValue = "forbidden";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using var verification = fixture.CreateDbContext();
        await Assert.That((await verification.Set<LegalDocumentVersion>()
            .SingleAsync(row => row.Id == versionId)).ReviewEvidenceReference).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnedPolicyChangesUseTheirPersistedRootTenant(bool foreign)
    {
        var rows = await SeedAsync();
        var id = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<TenantPolicySet>().Add(new TenantPolicySet
            {
                Id = id,
                TenantId = foreign ? rows.ForeignTenantId : rows.CurrentTenantId
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.TenantContext = new TenantScope(rows.CurrentTenantId);
            var policy = await context.Set<TenantPolicySet>().SingleAsync(row => row.Id == id);
            policy.Events.AllowUserSubmittedEvents.LocalValue = false;
            if (foreign)
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            else
                await context.SaveChangesAsync();
        }

        await using var verification = fixture.CreateDbContext();
        var persisted = await verification.Set<TenantPolicySet>().SingleAsync(row => row.Id == id);
        await Assert.That(persisted.Events.AllowUserSubmittedEvents.LocalValue).IsEqualTo(foreign);
    }

    [Test]
    public async Task SoftDeletedTrackedEntityCannotSatisfyGetById()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var group = new Group { FullName = "deleted target" };
        context.Groups.Add(group);
        await context.SaveChangesAsync();
        await new GenericRepository<Group, Guid>(context).Delete(group);

        await Assert.That(await new GenericRepository<Group, Guid>(context).GetById(group.Id)).IsNull();
    }

    [Test]
    public async Task ContextWideReasonDoesNotAuthorizeForeignTrackedWrites()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        context.EnableTenantFilterBypass("A reason is not current resource authority.");
        context.Attach(rows.Foreign);
        context.Entry(rows.Foreign).State = EntityState.Deleted;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task GenericTenantMutationRequiresTheExactTenantLease()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.ClearTenantFilterBypass();
        rows.Current.Value = "not authorized by an absent lease";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenericRepository<TenantSetting, Guid>(context).Update(rows.Current));
    }

    [Test]
    public async Task GenericReadsConstrainTenantColumnsEvenWithoutANamedModelFilter()
    {
        var rows = await SeedAsync();
        var current = new TenantPolicySet { Id = Guid.CreateVersion7(), TenantId = rows.CurrentTenantId };
        var foreign = new TenantPolicySet { Id = Guid.CreateVersion7(), TenantId = rows.ForeignTenantId };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<TenantPolicySet>().AddRange(current, foreign);
            await seed.SaveChangesAsync();
        }
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        var repository = new GenericRepository<TenantPolicySet, Guid>(context);

        await Assert.That(await repository.GetById(foreign.Id)).IsNull();
        await Assert.That(await repository.Exists(foreign.Id)).IsFalse();
        await Assert.That((await repository.GetAll()).Select(row => row.Id)).IsEquivalentTo(new[] { current.Id });
        await Assert.That((await repository.GetAllPaged(1, 10)).TotalCount).IsEqualTo(1);
    }

    [Test]
    public async Task PersistedKeyQueriesBindValuesAsParameters()
    {
        var rows = await SeedAsync();
        var observation = new KeyParameterObservation(rows.Current.Id);
        await using var context = fixture.CreateDbContext(observation);
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        var repository = new GenericRepository<TenantSetting, Guid>(context);

        await Assert.That(await repository.Exists(rows.Current.Id)).IsTrue();
        await Assert.That((await repository.GetById(rows.Current.Id))!.Value).IsEqualTo("original");
        await Assert.That(observation.ParameterizedCommands).IsEqualTo(2);
        await Assert.That(observation.InlinedKey).IsFalse();
    }

    [Test]
    public async Task NativeFenceRejectsCallerSelectedSqlIdentifier()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.CurrentTenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => RelationalEntityRowFence.AcquireAsync<TenantSetting>(
            context, rows.CurrentTenantId, row => EF.Property<Guid>(row, "id); DELETE FROM tenants; --"),
            rows.Current.Id, default));

        await Assert.That(await new GenericRepository<TenantSetting, Guid>(context).Exists(rows.Current.Id)).IsTrue();
        await transaction.RollbackAsync();
    }

    private async Task<Rows> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var current = new Tenant
        {
            FullName = "Current",
            Slug = $"current-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var foreign = new Tenant
        {
            FullName = "Foreign",
            Slug = $"foreign-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var currentSetting = new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = current.Id,
            Tenant = current,
            SettingKey = "phase21.current",
            Value = "original"
        };
        var foreignSetting = new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreign.Id,
            Tenant = foreign,
            SettingKey = "phase21.foreign",
            Value = "original"
        };
        context.Set<TenantSetting>().AddRange(currentSetting, foreignSetting);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        currentSetting.Tenant = null!;
        foreignSetting.Tenant = null!;
        return new Rows(current.Id, foreign.Id, currentSetting, foreignSetting);
    }

    private sealed record Rows(Guid CurrentTenantId, Guid ForeignTenantId,
        TenantSetting Current, TenantSetting Foreign);
    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class KeyParameterObservation(Guid key) : DbCommandInterceptor
    {
        public int ParameterizedCommands { get; private set; }
        public bool InlinedKey { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, key)))
                ParameterizedCommands++;
            InlinedKey |= command.CommandText.Contains(key.ToString("D"), StringComparison.OrdinalIgnoreCase);
            return ValueTask.FromResult(result);
        }
    }
}
