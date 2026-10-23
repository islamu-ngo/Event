using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalCredentialBindingTests
{
    public enum RejectedAuthority
    {
        TenantAdministrator,
        RevokedPlatformAdministrator
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, RejectedAuthority.TenantAdministrator)]
    [Arguments(IdentityDatabaseTopology.External, RejectedAuthority.TenantAdministrator)]
    [Arguments(IdentityDatabaseTopology.Colocated, RejectedAuthority.RevokedPlatformAdministrator)]
    [Arguments(IdentityDatabaseTopology.External, RejectedAuthority.RevokedPlatformAdministrator)]
    public async Task ReconciliationRequiresCurrentPlatformRoleDespiteTenantAuthorityOrEarlierApproval(
        IdentityDatabaseTopology topology, RejectedAuthority authority)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using var request = new Request(fixture.Lifecycle.Host.Services);
        await request.AuthenticateLocalAsync(await fixture.Lifecycle.SignInAsync(), fixture.InitiatorId);
        IAdminContext admin = request.Admin;
        if (authority == RejectedAuthority.RevokedPlatformAdministrator)
            await Assert.That(await admin.IsInstanceAdminAsync(CancellationToken)).IsTrue();
        await fixture.RemovePlatformRoleAsync();
        if (authority == RejectedAuthority.TenantAdministrator)
        {
            Guid tenantId = await fixture.GrantTenantAdministratorAsync();
            await Assert.That(await admin.IsTenantAdminAsync(tenantId, CancellationToken)).IsTrue();
        }
        else
            await Assert.That(await admin.IsInstanceAdminAsync(CancellationToken)).IsFalse();

        BaseCommandResponse<Guid> result = await request.Handler.ExecuteAsync(new(fixture.Receipt.OperationId), CancellationToken);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }

    [Test]
    public async Task RemovedExternalLoginCannotRetainCachedAdministratorIdentity()
    {
        // This independent OIDC account control keeps its external provider semantics;
        // the actual JWT validator, rather than a claims-only principal, issues custody.
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(AuthenticationProviderKind.Keycloak);
        Guid initiator = Guid.CreateVersion7();
        Guid subject = Guid.CreateVersion7();
        Guid externalLoginId = Guid.CreateVersion7();
        string email = $"admin-{initiator:N}@example.test";
        await using (var database = native.CreateDatabase())
        {
            var user = new User
            {
                Id = initiator,
                Pii = new UserPii { Email = email, FirstName = "External", LastName = "Administrator" },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = initiator,
                User = user,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            database.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = externalLoginId,
                UserId = initiator,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    issuer: native.ExternalIssuer, subject: subject.ToString("D")).Value,
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken);
        }
        LocalCredentialOperationReceipt receipt;
        await using (var seed = native.Services.CreateAsyncScope())
        {
            var result = await seed.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .CreatePendingAsync(new(Guid.CreateVersion7(), initiator,
                    $"binding-{Guid.CreateVersion7():N}@example.test", "Credential", "Owner"), CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            receipt = result.Receipt!;
        }
        await using var request = new Request(native.Services);
        await request.AuthenticateAsync(native.CreateExternalProviderToken(subject, email, emailVerified: true));
        await Assert.That(await request.Admin.ResolveUserIdAsync(CancellationToken)).IsEqualTo(initiator);
        await using (var revoke = native.CreateDatabase())
        {
            revoke.UserExternalLogins.Remove(await revoke.UserExternalLogins
                .SingleAsync(login => login.Id == externalLoginId, CancellationToken));
            await revoke.SaveChangesAsync(CancellationToken);
            await Assert.That(await new PlatformUserRoleRepository(revoke).IsUserPlatformAdmin(initiator)).IsTrue();
        }

        Guid? resolved = await request.Admin.ResolveUserIdAsync(CancellationToken);
        BaseCommandResponse<Guid> response = await request.Handler.ExecuteAsync(new(receipt.OperationId), CancellationToken);

        await Assert.That(resolved).IsNull();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var observer = native.CreateDatabase();
        await Assert.That(await observer.Users.AnyAsync(user => user.Id == receipt.ApplicationUserId, CancellationToken)).IsFalse();
        await Assert.That(await observer.Actors.AnyAsync(actor => actor.Id == receipt.PersonalActorId, CancellationToken)).IsFalse();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(login => login.Id == receipt.ExternalLoginId, CancellationToken)).IsFalse();
        await using var state = native.Services.CreateAsyncScope();
        var operation = (await state.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
            .ReadOperationAsync(receipt.OperationId, CancellationToken))!;
        await Assert.That(operation.IsCurrent).IsTrue();
        await Assert.That(operation.CredentialState).IsEqualTo(LocalCredentialState.ProvisioningPending);
        await Assert.That(operation.Receipt.ApplicationUserId).IsEqualTo(receipt.ApplicationUserId);
    }
}
