using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalCredentialBindingTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task MatchingExternalEmailNeverSubstitutesForSupportedLocalOwnership(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser credential = await fixture.ReadIdentityUserAsync();
        Guid externalUserId = Guid.CreateVersion7();
        Guid externalActorId = Guid.CreateVersion7();
        Guid externalLoginId = Guid.CreateVersion7();
        string externalKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            issuer: "https://external.example.test/realms/native", subject: Guid.CreateVersion7().ToString("D")).Value;
        await using (var database = fixture.Lifecycle.Native.CreateDatabase())
        {
            var user = new User
            {
                Id = externalUserId,
                Pii = new UserPii { Email = credential.Email!, FirstName = "External", LastName = "Owner" },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            database.Actors.Add(new Actor
            {
                Id = externalActorId,
                UserId = user.Id,
                User = user,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "External owner" },
                CreatedAt = DateTime.UtcNow
            });
            database.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = externalLoginId,
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = externalKey,
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken);
        }

        if (topology == IdentityDatabaseTopology.External)
            await AssertUnsupportedOwnerHasNoEffectsAsync(fixture);
        else
        {
            BaseCommandResponse<Guid> result = await fixture.ReconcileAsync();
            await Assert.That(result.IsSuccess).IsTrue();
            await fixture.AssertExactGraphAsync();
            await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        }
        await using var stored = fixture.Lifecycle.Native.CreateDatabase();
        UserExternalLogin external = await stored.UserExternalLogins.SingleAsync(
            login => login.Id == externalLoginId, CancellationToken);
        await Assert.That(external.UserId).IsEqualTo(externalUserId);
        await Assert.That(external.ProviderKey).IsEqualTo(externalKey);
        await Assert.That((await stored.Actors.SingleAsync(actor => actor.Id == externalActorId, CancellationToken)).UserId)
            .IsEqualTo(externalUserId);
        await Assert.That(await stored.Users.CountAsync(user => user.Pii.Email == credential.Email, CancellationToken))
            .IsEqualTo(topology == IdentityDatabaseTopology.Colocated ? 2 : 1);
        await Assert.That(fixture.Receipt.LocalSubjectId == externalUserId).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReconciliationRefusesPartialGraphsWithoutCompletingOrActivatingThem(bool includeActor)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        var receipt = fixture.Receipt;
        var before = await fixture.ReadIdentityUserAsync();
        var beforeOperation = await fixture.ReadOperationAsync();
        await using (var writer = fixture.Lifecycle.Native.CreateDatabase())
        {
            var user = new User
            {
                Id = receipt.ApplicationUserId,
                Pii = new UserPii { Email = before.Email!, FirstName = before.FirstName, LastName = before.LastName },
                EmailVerified = true,
                CreatedAt = receipt.CreatedAt,
                CreatedBy = receipt.InitiatingApplicationUserId
            };
            writer.Users.Add(user);
            if (includeActor)
                writer.Actors.Add(new Actor
                {
                    Id = receipt.PersonalActorId,
                    UserId = user.Id,
                    User = user,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Partial owner" },
                    CreatedAt = receipt.CreatedAt,
                    CreatedBy = receipt.InitiatingApplicationUserId
                });
            await writer.SaveChangesAsync(CancellationToken);
        }

        var result = await fixture.ReconcileAsync();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        await using var observer = fixture.Lifecycle.Native.CreateDatabase();
        await Assert.That(await observer.Users.CountAsync(row => row.Id == receipt.ApplicationUserId, CancellationToken)).IsEqualTo(1);
        await Assert.That(await observer.Actors.AnyAsync(row => row.Id == receipt.PersonalActorId, CancellationToken))
            .IsEqualTo(includeActor);
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == receipt.ExternalLoginId
            || row.UserId == receipt.ApplicationUserId, CancellationToken)).IsFalse();
        var after = await fixture.ReadIdentityUserAsync();
        await Assert.That(after.PasswordHash).IsEqualTo(before.PasswordHash);
        await Assert.That(after.SecurityStamp).IsEqualTo(before.SecurityStamp);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp).IsEqualTo(beforeOperation.ConcurrencyStamp);
    }
}
