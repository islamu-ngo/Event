using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments("revoked")]
    [Arguments("suspended")]
    [Arguments("expired-status")]
    [Arguments("expiry-boundary")]
    [Arguments("owner")]
    [Arguments("owner-kind")]
    [Arguments("authority-tenant")]
    [Arguments("deleted")]
    public async Task PreparedMachineMetadataCannotSurviveChangedNativeKeyAuthority(string change)
    {
        var (scope, user) = await SeedAsync();
        var resource = Resource(scope, user, 0, EventResourceAudienceKindEnum.Public);
        await SaveAsync(resource);
        var key = await SeedMachineKeyAsync(scope.TenantAId, user);
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(new ApiKeyPrincipalContext(key.KeyId, key.TenantId, key.OwnerType, key.OwnerId, ["events:read"])
        {
            CredentialSource = ApiKeyCredentialSource.Persisted
        });
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, machinePrincipal: machine);
        var accepted = await workflow.GetAsync(resource.Id, default);
        await Assert.That(accepted.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        bool delivered = false;
        var outcome = await accepted.Projection!.DiscloseAsync(async (value, _) =>
        {
            await using var writer = database.CreateIndependentContext();
            var target = writer.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .Where(row => row.Id == key.Id);
            int changed;
            switch (change)
            {
                case "revoked":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyStatusId,
                        (int)ExternalApiKeyStatusEnum.Revoked));
                    break;
                case "suspended":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyStatusId,
                        (int)ExternalApiKeyStatusEnum.Suspended));
                    break;
                case "expired-status":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyStatusId,
                        (int)ExternalApiKeyStatusEnum.Expired));
                    break;
                case "expiry-boundary":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, Now));
                    break;
                case "owner":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OwnerId, Guid.CreateVersion7()));
                    break;
                case "owner-kind":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyOwnerTypeId,
                        (int)ExternalApiKeyOwnerType.Organization));
                    break;
                case "authority-tenant":
                    changed = await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, scope.TenantBId));
                    break;
                case "deleted":
                    changed = await target.ExecuteDeleteAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            await Assert.That(changed).IsEqualTo(1);
            return value;
        }, (_, _) =>
        {
            delivered = true;
            return Task.CompletedTask;
        });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    private async Task<ExternalApiKey> SeedMachineKeyAsync(Guid tenantId, Guid ownerId)
    {
        var key = MachineKey(tenantId, ExternalApiKeyOwnerType.User, ownerId);
        await using var context = database.CreateContext();
        context.ExternalApiKeys.Add(key);
        await context.SaveChangesAsync();
        return key;
    }

    private static ExternalApiKey MachineKey(Guid tenantId, ExternalApiKeyOwnerType ownerType, Guid ownerId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = "Resource authority witness",
            KeyId = ApiKeyHashing.CreateKeyId(),
            SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
            Scopes = "events:read",
            OwnerType = ownerType,
            OwnerId = ownerId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!,
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(1)
        };
}
