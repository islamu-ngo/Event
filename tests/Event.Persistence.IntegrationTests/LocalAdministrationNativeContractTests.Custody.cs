using System.Data;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Identity;
using Explore.Persistence.Identity;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class LocalAdministrationNativeContractTests
{
    [Test]
    public async Task AppliedPendingGroupRemainsPrivateUntilAcknowledgedPhysicalSettlement()
    {
        var source = await SeedAsync();
        var request = Create(source);
        var accepted = Creation();
        await using (var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant))
        {
            await caller.Database.OpenConnectionAsync();
            await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
            await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
            await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, accepted, default);
            var applied = JsonNode.Parse(await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default))!;
            await Assert.That(applied["Outcome"]!.GetValue<string>()).IsEqualTo("Created");
            // A successful physical group is not a credential-release receipt.
            await owner.RollbackAsync();
        }
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.Set<LocalIdentityUser>().AnyAsync(row => row.Id == accepted.LocalSubjectId)).IsFalse();
        await Assert.That(await observer.Set<LocalIdentityCredentialOperation>().AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That(await observer.Set<IdentityUserToken<Guid>>().AnyAsync(row => row.UserId == accepted.LocalSubjectId)).IsFalse();
    }

    [Test]
    public async Task CompetingResetCannotBeAdoptedBetweenDiscloseReadAndAcquire()
    {
        var source = await SeedAsync();
        var (create, active) = await ActiveCredentialAsync(source);
        var original = active["Data"]!["LocalIdentityCredentialOperation"]![0]!;
        Guid subject = Guid.Parse(original["LocalSubjectId"]!.GetValue<string>());
        var reset = new Native.Request("Reset", LocalAdministrationStage.Reset, source.Actor, source.Tenant,
            Guid.CreateVersion7(), subject, create.OperationId,
            Guid.Parse(original["ConcurrencyStamp"]!.GetValue<string>()), Reason: "Original reset");
        var first = await StageAsync(reset, Mutation() with
        {
            PasswordHash = FreshHash(),
            PreviousOperationStamp = Guid.CreateVersion7()
        });
        var issued = first.Image["Data"]!["LocalIdentityCredentialOperation"]!.AsArray()
            .Single(row => row!["Id"]!.GetValue<string>() == reset.OperationId!.Value.ToString("D"))!;
        var disclose = reset with { Stage = LocalAdministrationStage.Disclose };
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string retained = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, disclose, default);
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(retained)!["Data"], first.Image["Data"])).IsTrue();

        var competing = reset with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedCurrentOperationId = reset.OperationId,
            ExpectedCurrentOperationConcurrencyStamp = Guid.Parse(issued["ConcurrencyStamp"]!.GetValue<string>()),
            Reason = "Competing reset"
        };
        var second = await StageAsync(competing, Mutation() with
        {
            PasswordHash = FreshHash(),
            PreviousOperationStamp = Guid.CreateVersion7()
        });
        await Assert.That(second.Image["Data"]!["LocalIdentityUser"]![0]!["PasswordHash"]!.GetValue<string>())
            .IsNotEqualTo(first.Image["Data"]!["LocalIdentityUser"]![0]!["PasswordHash"]!.GetValue<string>());
        var rejection = await Assert.ThrowsAsync<PostgresException>(() =>
            Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, disclose, retained, default));
        await Assert.That(rejection!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task ResetRetainsNonCredentialIdentityScalarsInItsClosedPostimage()
    {
        var source = await SeedAsync();
        var (create, active) = await ActiveCredentialAsync(source);
        var original = active["Data"]!["LocalIdentityCredentialOperation"]![0]!;
        Guid subject = Guid.Parse(original["LocalSubjectId"]!.GetValue<string>());
        await using (var writer = fixture.CreateDbContext())
        {
            var user = await writer.Set<LocalIdentityUser>().SingleAsync(row => row.Id == subject);
            user.AccessFailedCount = 3;
            user.TwoFactorEnabled = true;
            user.LockoutEnabled = false;
            user.LockoutEnd = new DateTimeOffset(Instant()).AddHours(1);
            await writer.SaveChangesAsync();
        }
        var reset = new Native.Request("Reset", LocalAdministrationStage.Reset, source.Actor, source.Tenant,
            Guid.CreateVersion7(), subject, create.OperationId,
            Guid.Parse(original["ConcurrencyStamp"]!.GetValue<string>()), Reason: "Retain identity options");
        var before = await StageAsync(reset with { Stage = LocalAdministrationStage.Disclose }, new());
        var result = await StageAsync(reset, Mutation() with
        {
            PasswordHash = FreshHash(),
            PreviousOperationStamp = Guid.CreateVersion7()
        });
        var oldUser = before.Image["Data"]!["LocalIdentityUser"]![0]!.AsObject();
        var newUser = result.Image["Data"]!["LocalIdentityUser"]![0]!.AsObject();
        foreach (var property in oldUser)
        {
            if (property.Key is "PasswordHash" or "SecurityStamp" or "ConcurrencyStamp" or "UpdatedAt")
                continue;
            await Assert.That(JsonNode.DeepEquals(property.Value, newUser[property.Key])).IsTrue();
        }
        await Assert.That(newUser["AccessFailedCount"]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(newUser["TwoFactorEnabled"]!.GetValue<bool>()).IsTrue();
        await Assert.That(newUser["LockoutEnabled"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task SuccessfulNativeCreationDoesNotGrantAnAmbientIdentityStore()
    {
        var source = await SeedAsync();
        var create = Create(source);
        var pending = await StageAsync(create, Creation());
        Guid subject = Guid.Parse(pending.Image["Data"]!["LocalIdentityUser"]![0]!["Id"]!.GetValue<string>());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            caller.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(row => row.Id == subject));
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<(Native.Request Create, JsonNode Image)> ActiveCredentialAsync(Seeded source)
    {
        var create = Create(source);
        var pending = await StageAsync(create, Creation());
        var operation = pending.Image["Data"]!["LocalIdentityCredentialOperation"]![0]!;
        var selected = create with
        {
            Stage = LocalAdministrationStage.Bind,
            ExpectedOperationConcurrencyStamp = Guid.Parse(operation["ConcurrencyStamp"]!.GetValue<string>())
        };
        await StageAsync(selected, new(UserConcurrencyStamp: Guid.CreateVersion7(), ActorConcurrencyStamp: Guid.CreateVersion7()));
        var active = await StageAsync(selected with { Stage = LocalAdministrationStage.Activate }, Mutation());
        return (create, active.Image);
    }
}
