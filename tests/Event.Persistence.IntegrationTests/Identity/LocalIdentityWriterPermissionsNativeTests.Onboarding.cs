using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    public async Task GlobalEnrollmentReadsBindingStateWithoutUserOrActorProfiles()
    {
        await using var fixture = new NativeFixture(IdentityDatabaseTopology.Colocated);
        await fixture.InitializeAsync();
        Guid subject = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        await using (var migrator = (ExploreDbContext)CreateContext(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator), LocalIdentityWriterInventory.Store.Application))
        {
            await migrator.Database.OpenConnectionAsync();
            await ManagedTenantWorkerCreationPermissionsNativeTests.EnsurePolicyProjectionAsync(migrator);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            migrator.Users.Add(new User
            {
                Id = subject,
                Pii = new UserPii
                {
                    UserId = subject,
                    Email = $"{subject:N}@example.invalid",
                    FirstName = "Enrollment",
                    LastName = "Witness"
                }
            });
            migrator.Actors.Add(new Actor
            {
                Id = actorId,
                UserId = subject,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Enrollment witness" }
            });
            await migrator.SaveChangesAsync();
        }

        await using var original = (ExploreDbContext)CreateContext(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), LocalIdentityWriterInventory.Store.Application);
        await original.Database.OpenConnectionAsync();
        await RequireSessionAsync(original, fixture.NativeQueueEnrollment.GlobalLogin);
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
            original, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var users = new UserRepository(original);
        var actors = new ActorRepository(original);
        var userProfile = await Assert.ThrowsAsync<PostgresException>(() => users.GetById(subject));
        await Assert.That(userProfile!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var actorProfile = await Assert.ThrowsAsync<PostgresException>(() => actors.GetActorByUserId(subject));
        await Assert.That(actorProfile!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        var connector = (NpgsqlConnection)original.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var contextId = original.ContextId;
        await original.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var physical = transaction.GetDbTransaction();
            User? user = await users.GetForIdentityEnrollmentAsync(subject, CancellationToken.None);
            Actor? actor = await actors.GetForIdentityEnrollmentAsync(subject, CancellationToken.None);
            await Assert.That(user).IsNotNull();
            await Assert.That(user!.Id).IsEqualTo(subject);
            await Assert.That(user.IsDeleted).IsFalse();
            await Assert.That(actor).IsNotNull();
            await Assert.That(actor!.Id).IsEqualTo(actorId);
            await Assert.That(actor.UserId).IsEqualTo(subject);
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.IsDeleted).IsFalse();
            await Assert.That(actor.IsSuspended).IsFalse();
            await Assert.That(await users.GetForIdentityEnrollmentAsync(Guid.CreateVersion7(), CancellationToken.None)).IsNull();
            await Assert.That(await actors.GetForIdentityEnrollmentAsync(Guid.CreateVersion7(), CancellationToken.None)).IsNull();
            await Assert.That(original.ContextId).IsEqualTo(contextId);
            await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(original.ChangeTracker.HasChanges()).IsFalse();
            await RequireSessionAsync(original, fixture.NativeQueueEnrollment.GlobalLogin);
            await transaction.RollbackAsync(CancellationToken.None);
        });
    }
}
