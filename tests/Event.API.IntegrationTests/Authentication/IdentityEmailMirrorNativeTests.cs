using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class IdentityEmailMirrorNativeTests(PostgreSqlContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RestrictedMirrorPreservesClearedPrivateRevisionAndCallerSettlement(bool commit)
    {
        Guid userId = await SeedAsync();
        await using var contexts = await CreateWriterAsync();
        var context = contexts.Application;
        await RequireWriterRoleAsync(context);
        Guid originalStamp = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var human = await humanGate.AcquireAsync(Token);
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryGate.AcquireAsync(Token);
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            var physical = transaction.GetDbTransaction();
            IIdentityEmailMirrorWriter writer = new IdentityEmailMirrorWriter(context, new UserRepository(context));
            await using var prepared = await writer.PrepareAsync(userId, Token);
            Guid stamp = context.ChangeTracker.Entries<User>().Single().Entity.ConcurrencyStamp;
            await prepared.ApplyAsync("mirrored@example.test", true, Token);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(ReferenceEquals(physical, transaction.GetDbTransaction())).IsTrue();
            var profile = context.ChangeTracker.Entries<UserPii>().Single().Entity;
            await Assert.That(profile.ReadBirthDateRevision()).IsEqualTo(1L);
            if (commit)
                await transaction.CommitAsync(Token);
            else
                await transaction.RollbackAsync(Token);
            return stamp;
        });

        await using var observation = postgres.CreateDbContext();
        observation.BindTenantLease();
        var persisted = (await new UserRepository(observation).GetById(userId))!;
        await Assert.That(persisted.Email).IsEqualTo(commit ? "mirrored@example.test" : OriginalEmail(userId));
        await Assert.That(persisted.EmailVerified).IsEqualTo(commit);
        if (commit)
            await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(originalStamp);
        else
            await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(persisted.Pii.FirstName).IsEqualTo("Native");
        await Assert.That(persisted.Pii.LastName).IsEqualTo("Mirror");
        await Assert.That(persisted.Pii.ReadBirthDateRevision()).IsEqualTo(1L);
        await Assert.That(persisted.Pii.ReadDeclaredBirthDate()).IsNull();
        await Assert.That(persisted.Pii.ReadBirthDateCollectionReceiptId()).IsNull();
    }

    [Test]
    public async Task OriginalRestrictedRoleRejectsInTransactionAccountDriftAndRollsBackMirrorWork()
    {
        Guid userId = await SeedAsync();
        await using var contexts = await CreateWriterAsync();
        var context = contexts.Application;
        await RequireWriterRoleAsync(context);
        Guid originalStamp = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var human = await humanGate.AcquireAsync(Token);
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryGate.AcquireAsync(Token);
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            var physical = transaction.GetDbTransaction();
            IIdentityEmailMirrorWriter writer = new IdentityEmailMirrorWriter(context, new UserRepository(context));
            await using var prepared = await writer.PrepareAsync(userId, Token);
            Guid stamp = context.ChangeTracker.Entries<User>().Single().Entity.ConcurrencyStamp;
            // Advance storage through the same declared role, without replacing the
            // prepared tracked snapshot or racing a cooperating source-gate holder.
            Guid advancedStamp = Guid.CreateVersion7();
            int changed = await context.Users.Where(user => user.Id == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    user => user.ConcurrencyStamp, advancedStamp), Token);
            await Assert.That(changed).IsEqualTo(1);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                prepared.ApplyAsync("stale@example.test", true, Token));
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(ReferenceEquals(physical, transaction.GetDbTransaction())).IsTrue();
            await transaction.RollbackAsync(Token);
            return stamp;
        });

        await using var observation = postgres.CreateDbContext();
        observation.BindTenantLease();
        var persisted = (await new UserRepository(observation).GetById(userId))!;
        await Assert.That(persisted.Email).IsEqualTo(OriginalEmail(userId));
        await Assert.That(persisted.EmailVerified).IsEqualTo(false);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(persisted.Pii.ReadBirthDateRevision()).IsEqualTo(1L);
    }

    [Test]
    public async Task RestrictedRoleCannotReadPrivateDeclarationOrUseFullProfileRepositoryRead()
    {
        Guid userId = await SeedAsync();
        await using var contexts = await CreateWriterAsync();
        var context = contexts.Application;
        await RequireWriterRoleAsync(context);
        var fullProfile = await Assert.ThrowsAsync<PostgresException>(() =>
            new UserRepository(context).GetById(userId));
        await Assert.That(fullProfile?.SqlState).IsEqualTo("42501");

        foreach (string property in new[] { "_dateOfBirth", "_birthDateCollectionReceiptId" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                if (property == "_dateOfBirth")
                    await context.UserPii.Where(pii => pii.UserId == userId)
                        .Select(pii => EF.Property<DateOnly?>(pii, "_dateOfBirth")).SingleAsync(Token);
                else
                    await context.UserPii.Where(pii => pii.UserId == userId)
                        .Select(pii => EF.Property<Guid?>(pii, "_birthDateCollectionReceiptId")).SingleAsync(Token);
            });
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DisposedPreparationAllowsFreshProfileReadsAndPreservesUnrelatedPendingWrites(bool preexistingAccount)
    {
        Guid userId = await SeedAsync();
        Guid otherId = await SeedAsync();
        await using var context = postgres.CreateDbContext();
        context.ClearTenantFilterBypass();
        context.BindTenantLease();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var human = await humanGate.AcquireAsync(Token);
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryGate.AcquireAsync(Token);
            var repository = new UserRepository(context);
            User? original = preexistingAccount
                ? await context.Users.IgnoreAutoIncludes().SingleAsync(user => user.Id == userId, Token)
                : null;
            var other = await context.Users.IgnoreAutoIncludes().SingleAsync(user => user.Id == otherId, Token);
            other.EmailVerified = true;
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            IIdentityEmailMirrorWriter writer = new IdentityEmailMirrorWriter(context, repository);
            await using var prepared = await writer.PrepareAsync(userId, Token);
            await prepared.DisposeAsync();
            await prepared.DisposeAsync();
            await context.UserPii.Where(pii => pii.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(pii => pii.FirstName, "Fresh"), Token);
            var fresh = (await repository.GetById(userId))!;
            await Assert.That(fresh.FirstName).IsEqualTo("Fresh");
            await Assert.That(fresh.Pii.ReadBirthDateRevision()).IsEqualTo(1L);
            if (original is not null)
                await Assert.That(fresh).IsSameReferenceAs(original);
            await context.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        });
        await using var observation = postgres.CreateDbContext();
        observation.BindTenantLease();
        var persisted = (await new UserRepository(observation).GetById(otherId))!;
        await Assert.That(persisted.EmailVerified).IsEqualTo(true);
    }

    private async Task<Guid> SeedAsync()
    {
        await postgres.PrepareLocalIdentityWriterAsync();
        Guid userId = Guid.CreateVersion7();
        await using (var seed = postgres.CreateDbContext())
        {
            seed.BindTenantLease();
            await seed.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var humanGate = new HumanSecuritySourceGate(seed);
                await using var human = await humanGate.AcquireAsync(Token);
                await using var discoveryGate = new EventDiscoveryEnrollmentGate(seed);
                await using var discovery = await discoveryGate.AcquireAsync(Token);
                await using var transaction = await seed.Database.BeginTransactionAsync(Token);
                await new UserRepository(seed).Create(new User
                {
                    Id = userId,
                    Pii = new UserPii
                    {
                        UserId = userId,
                        Email = OriginalEmail(userId),
                        FirstName = "Native",
                        LastName = "Mirror"
                    },
                    EmailVerified = false,
                    CreatedAt = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)
                });
                await transaction.CommitAsync(Token);
            });
        }

        await using (var correction = postgres.CreateDbContext())
        {
            correction.BindTenantLease();
            await correction.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var humanGate = new HumanSecuritySourceGate(correction);
                await using var human = await humanGate.AcquireAsync(Token);
                await using var discoveryGate = new EventDiscoveryEnrollmentGate(correction);
                await using var discovery = await discoveryGate.AcquireAsync(Token);
                await using var transaction = await correction.Database.BeginTransactionAsync(Token);
                var user = (await new UserRepository(correction).GetById(userId))!;
                await Assert.That(user.Pii.ReadBirthDateRevision()).IsEqualTo(0L);
                // Advance an existing private revision through its domain transition;
                // no collection authority or nonnull declaration is fabricated.
                user.Pii.ClearDeclaredBirthDate(0);
                await correction.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
            });
        }
        return userId;
    }

    private Task<LocalIdentityWriterDbContextFactory.Contexts> CreateWriterAsync()
    {
        var options = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var factory = new LocalIdentityWriterDbContextFactory(options,
            applicationEnrollment: new PostgresLocalIdentityWriterEnrollment(
                postgres.NativeQueueEnrollment.MigratorLogin,
                postgres.NativeQueueEnrollment.RuntimeLogin,
                options.Username!,
                postgres.NativeQueueEnrollment.GlobalLogin));
        return factory.CreateAsync(null, Token);
    }

    private async Task RequireWriterRoleAsync(ExploreDbContext context)
    {
        string selected = await context.Database.SqlQueryRaw<string>(
            "SELECT current_user::text AS \"Value\" WHERE current_user=session_user").SingleAsync(Token);
        await Assert.That(selected).IsEqualTo(
            postgres.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter).Username);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    private static string OriginalEmail(Guid userId) => $"native-mirror-{userId:N}@example.test";
}
