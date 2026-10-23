using Cerbos.Api.V1.Effect;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class PlatformApiKeyOriginalOwnerTests
{
    [Test]
    public async Task CurrentInstanceAdministratorCanManageAnotherAdministratorsKey()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        Guid originalOwner = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
            await seed.ExternalApiKeys.Where(row => row.Id == key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.OwnerId, originalOwner));
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var operations = ActivatorUtilities.CreateInstance<InstanceApiKeyOperations>(request.Services, request.Factory);
        var details = ActivatorUtilities.CreateInstance<GetExternalApiKeyDetailsRequestHandler>(request.Services, operations);
        var revoke = ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(request.Services, operations);
        await Assert.That((await operations.ReadAsync(fixture.Binding.LocalSubjectId, key, default)).Single().OwnerId).IsEqualTo(originalOwner);
        await Assert.That(await details.QueryAsync(new(key), default)).IsNotNull();
        await Assert.That(await revoke.ExecuteAsync(new(key), default)).IsTrue();
        await Assert.That(await revoke.ExecuteAsync(new(key), default)).IsTrue();
        var persisted = await details.QueryAsync(new(key), default);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted!.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
    }

    [Test]
    public async Task DedicatedMetadataListIsSelfOwnedWhileExactAdministrativeReadCanCrossOwners()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid mine = await SeedAsync(fixture);
        Guid other = Guid.CreateVersion7();
        Guid tenant = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach ((Guid id, Guid? tenantId, Guid owner) in new[]
            {
                (other, (Guid?)null, Guid.CreateVersion7()),
                (tenant, (Guid?)PlatformDefaults.DefaultTenantId, fixture.Binding.LocalSubjectId)
            })
            {
                seed.ExternalApiKeys.Add(new ExternalApiKey
                {
                    Id = id,
                    TenantId = tenantId,
                    Name = "Separated namespace",
                    KeyId = id.ToString("N"),
                    SecretHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                    Scopes = "events:read",
                    OwnerType = tenantId is null ? ExternalApiKeyOwnerType.InstanceAdmin : ExternalApiKeyOwnerType.User,
                    OwnerId = owner,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                    ExternalApiKeyCreditPeriod = null!
                });
            }
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var operations = ActivatorUtilities.CreateInstance<InstanceApiKeyOperations>(request.Services, request.Factory);
        await Assert.That((await operations.ReadAsync(fixture.Binding.LocalSubjectId, null, default)).Select(row => row.Id))
            .IsEquivalentTo([mine]);
        var exact = (await operations.ReadAsync(fixture.Binding.LocalSubjectId, other, default)).Single();
        await Assert.That(exact.Id).IsEqualTo(other);
        await Assert.That(exact.SecretHash).IsEqualTo(string.Empty);
        await Assert.That(exact.LastUsedIp).IsNull();
        await Assert.That(await operations.ReadAsync(fixture.Binding.LocalSubjectId, tenant, default)).IsEmpty();
        await Assert.ThrowsAsync<AuthorizationException>(() => operations.ReadAsync(Guid.CreateVersion7(), other, default));
    }

    [Test]
    public async Task DedicatedScopeAcquisitionDoesNotFreezeAnAdministratorGrant()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using var scope = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, key, default);
        await using (var writer = fixture.Native.CreateDatabase())
            await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ExecuteAsync(InstanceApiKeyAction.Revoke,
            token => scope.RevokeAsync(fixture.Clock.GetUtcNow().UtcDateTime, token), default));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == key)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    public async Task DedicatedPolicyMutationRollsBackWithItsOwnedTransaction()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        Guid originalOwner = Guid.CreateVersion7();
        await using (var writer = fixture.Native.CreateDatabase())
            await writer.ExternalApiKeys.Where(row => row.Id == key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.OwnerId, originalOwner));
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using var scope = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, key, default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.ExecuteAsync<bool>(InstanceApiKeyAction.Update, async token =>
        {
            await scope.UpdatePolicyAsync("Rolled back", "events:read", null, fixture.Clock.GetUtcNow().UtcDateTime, token);
            throw new OperationCanceledException("Abort after the real fixed mutation.");
        }, default));
        var operations = ActivatorUtilities.CreateInstance<InstanceApiKeyOperations>(request.Services, request.Factory);
        var metadata = (await operations.ReadAsync(fixture.Binding.LocalSubjectId, key, default)).Single();
        await Assert.That(metadata.Name).IsEqualTo("Original platform owner");
        await Assert.That(metadata.OwnerId).IsEqualTo(originalOwner);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task SelectedInstanceCerbosOutageCannotFallBackForDedicatedKeyOperations(int operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var sent = ConfigurePolicy(request, () => Task.CompletedTask, Effect.Allow, forceCerbos: true, unavailable: true);
        var operations = ActivatorUtilities.CreateInstance<InstanceApiKeyOperations>(request.Services, request.Factory);
        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(async () =>
        {
            switch (operation)
            {
                case 0: await operations.ReadAsync(fixture.Binding.LocalSubjectId, key, default); break;
                case 1:
                    await operations.UpdateAsync(fixture.Binding.LocalSubjectId, key,
                    new() { Metadata = new() { Name = "Denied change" } }, default); break;
                case 2: await operations.RevokeAsync(fixture.Binding.LocalSubjectId, key, default); break;
                case 3: await operations.ReportAsync(fixture.Binding.LocalSubjectId, new(2026, 1, 1), new(2026, 1, 31), default); break;
            }
        });
        await Assert.That(sent.Count).IsGreaterThan(0);
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == key);
        await Assert.That(persisted.Name).IsEqualTo("Original platform owner");
        await Assert.That(persisted.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }
}
