using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Handlers.Queries;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class ApiKeyManagementCommitAdmissionTests
{
    [Test]
    public async Task KeyPolicyAndRevocationRetainCurrentOwnerSuccess()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var changed = await ActivatorUtilities.CreateInstance<UpdateExternalApiKeyPolicyCommandHandler>(services)
            .ExecuteAsync(new UpdateExternalApiKeyPolicyCommand
            {
                ExternalApiKeyId = keyId,
                ExternalApiKeyPolicyDto = new() { Metadata = new() { Name = "Current owner policy" } }
            }, default);
        await Assert.That(changed.IsSuccess).IsTrue();
        var revoke = ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(
            services, Preparation(services, fixture.Clock));
        await Assert.That(await revoke.ExecuteAsync(new(keyId), default)).IsTrue();
        // Idempotent revocation still passes the same original-human final gate.
        await Assert.That(await revoke.ExecuteAsync(new(keyId), default)).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId);
        await Assert.That(persisted.Name).IsEqualTo("Current owner policy");
        await Assert.That(persisted.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
    }

    [Test]
    public async Task TrackedOwnerSubstitutionCannotAuthorizeKeyRevocation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        var otherLogin = await fixture.Native.SeedLocalUserAsync(emailConfirmed: true);
        Guid otherId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            otherId = await seed.Set<LocalIdentityUser>().Where(row => row.Email == otherLogin.Identifier)
                .Select(row => row.Id).SingleAsync();
            await seed.ExternalApiKeys.Where(row => row.Id == keyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.OwnerId, otherId));
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        var tracked = await context.ExternalApiKeys.SingleAsync(row => row.Id == keyId);
        tracked.OwnerId = fixture.Binding.LocalSubjectId;
        var handler = ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(
            services, Preparation(services, fixture.Clock));
        await Assert.That(await handler.ExecuteAsync(new(keyId), default)).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId);
        await Assert.That(persisted.OwnerId).IsEqualTo(otherId);
        await Assert.That(persisted.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    public async Task CommittedTenantGrantRevocationPreventsKeyMutation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        Guid grantId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = await seed.TenantUsers.SingleAsync(row => row.UserId == fixture.Binding.LocalSubjectId);
            var grant = new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUserRoleGrants.Add(grant);
            grantId = grant.Id;
            var key = await seed.ExternalApiKeys.SingleAsync(row => row.Id == keyId);
            key.OwnerType = ExternalApiKeyOwnerType.Tenant;
            key.OwnerId = member.TenantId;
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        bool revokedBeforeOwner = false;
        var owner = new BeforeOwner(services.GetRequiredService<ISerializableAttemptOwner>(), async token =>
        {
            await using var revocation = fixture.Native.CreateDatabase();
            await revocation.TenantUserRoleGrants.Where(row => row.Id == grantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                    fixture.Clock.GetUtcNow().UtcDateTime), token);
            revokedBeforeOwner = true;
        });
        var handler = ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(
            services, owner, Preparation(services, fixture.Clock));
        await Assert.That(await handler.ExecuteAsync(new(keyId), default)).IsFalse();
        await Assert.That(revokedBeforeOwner).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == keyId)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
        await Assert.That(await observer.TenantUserRoleGrants.Where(row => row.Id == grantId)
            .Select(row => row.RevokedAt).SingleAsync()).IsNotNull();
    }

    [Test]
    public async Task RequestedTenantUsageReportDoesNotReuseTheAmbientTenant()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid ambientKeyId = await SeedAsync(fixture);
        Guid selectedKeyId = Guid.CreateVersion7();
        Guid selectedTenantId = Guid.CreateVersion7();
        DateOnly from = new(2026, 1, 1);
        DateOnly to = new(2026, 1, 31);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            seed.Tenants.Add(new Tenant
            {
                Id = selectedTenantId,
                FullName = "Selected report tenant",
                Slug = $"report-{selectedTenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            var selected = new ExternalApiKey
            {
                Id = selectedKeyId,
                TenantId = selectedTenantId,
                Name = "Selected usage",
                KeyId = Guid.CreateVersion7().ToString("N"),
                SecretHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.User,
                OwnerId = fixture.Binding.LocalSubjectId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            };
            seed.ExternalApiKeys.Add(selected);
            seed.ExternalApiKeyQuotas.AddRange(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = ambientKeyId,
                ExternalApiKey = null!,
                PeriodStart = from,
                PeriodEnd = to,
                RequestCount = 11,
                CreditsUsed = 1
            }, new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = selectedKeyId,
                ExternalApiKey = selected,
                PeriodStart = from,
                PeriodEnd = to,
                RequestCount = 7,
                CreditsUsed = 2
            });
            var setting = await seed.SystemSettings.SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
            setting.Value = "\"local\"";
            await seed.SaveChangesAsync();
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanApiKeyManagementPreparation()));
        await using var request = new Request(host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var report = ActivatorUtilities.CreateInstance<GetExternalApiKeyUsageReportRequestHandler>(request.Services);
        var rows = await report.QueryAsync(new() { TenantId = selectedTenantId, From = from, To = to }, default);
        await Assert.That(rows.Select(row => row.ApiKeyId)).IsEquivalentTo([selectedKeyId]);
        await Assert.That(rows.Single().TotalRequestCount).IsEqualTo(7L);
        await Assert.That(request.Services.GetRequiredService<ITenantContext>().TenantId)
            .IsEqualTo(PlatformDefaults.DefaultTenantId);
    }

    private sealed class BeforeOwner(ISerializableAttemptOwner inner, Func<CancellationToken, Task> before)
        : ISerializableAttemptOwner
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            await before(cancellationToken);
            return await inner.ExecuteAsync(operation, cancellationToken);
        }
    }
}
