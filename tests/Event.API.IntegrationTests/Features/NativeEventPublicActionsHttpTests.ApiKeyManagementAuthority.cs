using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task KeyManagementBypassCannotReadAnotherTenantsKey()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var key = ManagedKey(data.ForeignTenantId, data.OwnerId, ExternalApiKeyOwnerType.User);
        await using (var foreign = await scope.ServiceProvider
                         .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync())
        {
            foreign.TenantContext = new AuthorityTenant(data.ForeignTenantId);
            foreign.ExternalApiKeys.Add(key);
            await foreign.SaveChangesAsync();
        }

        var repository = scope.ServiceProvider.GetRequiredService<IExternalApiKeyRepository>();
        await Assert.That(await repository.GetForTenantAsync(key.Id)).IsNull();
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(scope.ServiceProvider);
        await Assert.That(await handler.ExecuteAsync(new(key.Id), default)).IsFalse();
    }

    private static ExternalApiKey ManagedKey(Guid? tenantId, Guid ownerId, ExternalApiKeyOwnerType ownerType) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Name = $"managed-{Guid.CreateVersion7():N}",
        KeyId = Guid.CreateVersion7().ToString("N"),
        SecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        Scopes = "events:read",
        OwnerType = ownerType,
        OwnerId = ownerId,
        ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
        ExternalApiKeyStatus = null!,
        ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
        ExternalApiKeyCreditPeriod = null!
    };
}
