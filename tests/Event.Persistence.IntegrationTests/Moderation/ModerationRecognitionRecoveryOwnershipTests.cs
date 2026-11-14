using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false, "transaction")]
    [Arguments(false, "authority")]
    [Arguments(false, "save")]
    [Arguments(false, "commit")]
    [Arguments(true, "none")]
    [Arguments(true, "save")]
    [Arguments(true, "commit")]
    public async Task Recognition_recovery_keeps_original_native_completion_and_rolls_back_failed_binding(
        bool colocated, string failure)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(sanctionDuration: TimeSpan.FromMinutes(1));
        var save = new RecognitionRecoverySaveBoundary();
        var commit = new RecognitionRecoveryCommitBoundary();
        await using var context = fixture.Database.MaintenanceContext(save, commit);
        var originalRepository = fixture.Repository(context);
        await originalRepository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(1);

        EmbeddedPrivacyErasureAuthorityRepository authority = fixture.Retained.Repository;
        if (colocated)
        {
            var factory = new RecognitionCoLocatedAuthorityFactory(context.Database.GetDbConnection().DataSource, commit);
            authority = new(factory, fixture.Clock, Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        }
        var composition = fixture.LifecycleFixture.Bind(context, authority, authority);
        var repository = new ModerationRecognitionErasureRepository(context, composition.Provider, fixture.Clock,
            authority, authority, composition.Repository);
        var fact = await authority.AppendAsync(fixture.Request);
        IDbContextTransaction? replacement = null;
        DbTransaction? savedTransaction = null;
        bool borrowed = false;
        save.AfterBinding = async token =>
        {
            var original = context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Recognition repair lost its transaction before the save observation.");
            savedTransaction = original.GetDbTransaction();
            borrowed = context.IdentityFenceOwnsTransaction;
            if (failure == "save") throw new IOException("Injected recognition recovery save failure.");
            if (failure == "transaction")
            {
                await original.DisposeAsync();
                replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            }
            if (failure == "authority")
                await authority.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1), token);
        };
        commit.Armed = true;
        commit.Fail = failure == "commit";
        try
        {
            if (failure == "none")
                await Assert.That(await repository.ReconcileRetainedAsync(fact, CancellationToken.None)).IsTrue();
            else if (failure is "save" or "commit")
                await Assert.ThrowsAsync<IOException>(() => repository.ReconcileRetainedAsync(fact, CancellationToken.None));
            else
            {
                var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.ReconcileRetainedAsync(fact, CancellationToken.None));
                if (failure == "transaction")
                    await Assert.That(refusal?.Message).IsEqualTo("moderation_recognition_recovery_owner_changed");
            }
        }
        finally
        {
            if (replacement is not null) await replacement.DisposeAsync();
        }
        await Assert.That(save.Observed).IsTrue();
        await Assert.That(borrowed).IsEqualTo(colocated);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
        if (failure is "none" or "commit")
        {
            await Assert.That(commit.Attempts).IsEqualTo(1);
            await Assert.That(ReferenceEquals(commit.Physical, savedTransaction)).IsTrue();
            await Assert.That(commit.AuthorityOwner).IsEqualTo(colocated);
        }
        else await Assert.That(commit.Attempts).IsEqualTo(0);
        await using var verification = fixture.Database.MaintenanceContext();
        var pending = (await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(pending.AuthoritySequence).IsEqualTo(failure == "none" ? fact.AuthoritySequence : (long?)null);
        await Assert.That(pending.MaterialReceipt).IsNull();
        await Assert.That(pending.Material.Count).IsEqualTo(0);
        await Assert.That((await authority.GetStateAsync()).HighWaterSequence).IsEqualTo(fact.AuthoritySequence);
        await Assert.That((await authority.ReadAfterAsync(0, 100)).Single().IntentId).IsEqualTo(fact.IntentId);
    }

    private sealed class RecognitionRecoverySaveBoundary : SaveChangesInterceptor
    {
        public Func<CancellationToken, Task>? AfterBinding { get; set; }
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && AfterBinding is not null && eventData.Context!.ChangeTracker
                .Entries<ModerationRecognitionPending>().Any(entry => entry.Entity.AuthoritySequence is not null))
            {
                Observed = true;
                await AfterBinding(cancellationToken);
            }
            return result;
        }
    }

    private sealed class RecognitionRecoveryCommitBoundary : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Fail { get; set; }
        public int Attempts { get; private set; }
        public DbTransaction? Physical { get; private set; }
        public bool AuthorityOwner { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Attempts++;
                Physical = transaction;
                AuthorityOwner = eventData.Context is EmbeddedPrivacyErasureAuthorityDbContext;
                if (Fail) throw new IOException("Injected recognition recovery commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecognitionCoLocatedAuthorityFactory(string path, IInterceptor interceptor)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>();
            EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(options, new PrimaryDatabaseConnectionOptions
            { Role = PrimaryDatabaseRole.Global, Provider = PrimaryDatabaseProvider.Sqlite, Database = path });
            return new(options.AddInterceptors(interceptor).Options);
        }
        public Task<EmbeddedPrivacyErasureAuthorityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
