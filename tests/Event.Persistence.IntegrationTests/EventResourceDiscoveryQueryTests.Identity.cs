using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments("key")]
    [Arguments("owner-kind")]
    [Arguments("owner")]
    [Arguments("authority-tenant")]
    public async Task MachineCursorCannotContinueUnderDifferentExactPrincipalCoordinates(string coordinate)
    {
        var (scope, user) = await SeedAsync();
        await SaveAsync(Resource(scope, user, 0, EventResourceAudienceKindEnum.Public),
            Resource(scope, user, 1, EventResourceAudienceKindEnum.Public));
        var key = await SeedMachineKeyAsync(scope.TenantAId, user);
        var original = new ApiKeyPrincipalContext(key.KeyId, key.TenantId, key.OwnerType, key.OwnerId, [])
        {
            CredentialSource = ApiKeyCredentialSource.Persisted
        };
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(original);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, machinePrincipal: machine);
        var accepted = await workflow.ListAsync(scope.EventAId, 1, null, default);
        await Assert.That(accepted.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        var first = await DiscloseAsync(accepted.Projection);
        await Assert.That(first.Items.Length).IsEqualTo(1);
        await Assert.That(first.NextCursor).IsNotNull();

        machine.Current.Returns(await ChangedPrincipalAsync(original, coordinate, scope.TenantBId));
        var supported = await workflow.ListAsync(scope.EventAId, 1, null, default);
        await Assert.That(supported.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await DiscloseAsync(supported.Projection);
        var continuation = await workflow.ListAsync(scope.EventAId, 1, first.NextCursor, default);
        await Assert.That(continuation.Failure).IsEqualTo(EventResourceAudienceFailure.InvalidRequest);
        await Assert.That(continuation.Projection).IsNull();
    }

    [Test]
    [Arguments("key")]
    [Arguments("owner-kind")]
    [Arguments("owner")]
    [Arguments("authority-tenant")]
    public async Task MachineProjectionCannotDeliverUnderDifferentExactPrincipalCoordinates(string coordinate)
    {
        var (scope, user) = await SeedAsync();
        var resource = Resource(scope, user, 0, EventResourceAudienceKindEnum.Public);
        await SaveAsync(resource);
        var key = await SeedMachineKeyAsync(scope.TenantAId, user);
        var original = new ApiKeyPrincipalContext(key.KeyId, key.TenantId, key.OwnerType, key.OwnerId, [])
        {
            CredentialSource = ApiKeyCredentialSource.Persisted
        };
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(original);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, machinePrincipal: machine);
        var accepted = await workflow.GetAsync(resource.Id, default);
        await Assert.That(accepted.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        bool delivered = false;
        var outcome = await accepted.Projection!.DiscloseAsync(async (value, _) =>
        {
            machine.Current.Returns(await ChangedPrincipalAsync(original, coordinate, scope.TenantBId));
            return value;
        }, (_, _) =>
        {
            delivered = true;
            return Task.CompletedTask;
        });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        var supported = await workflow.GetAsync(resource.Id, default);
        await Assert.That(supported.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await DiscloseAsync(supported.Projection);
    }

    private async Task<ApiKeyPrincipalContext> ChangedPrincipalAsync(
        ApiKeyPrincipalContext original, string coordinate, Guid alternativeTenantId)
    {
        var changed = coordinate switch
        {
            "key" => original with { KeyId = ApiKeyHashing.CreateKeyId() },
            "owner-kind" => original with { OwnerType = ExternalApiKeyOwnerType.Organization },
            "owner" => original with { OwnerId = Guid.CreateVersion7() },
            "authority-tenant" => original with { TenantId = alternativeTenantId },
            _ => throw new ArgumentOutOfRangeException(nameof(coordinate))
        };
        await using var context = database.CreateContext();
        if (coordinate == "key")
        {
            var key = MachineKey(changed.TenantId!.Value, changed.OwnerType, changed.OwnerId);
            key.KeyId = changed.KeyId;
            context.ExternalApiKeys.Add(key);
        }
        else
        {
            var key = await context.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .SingleAsync(value => value.KeyId == original.KeyId);
            key.TenantId = changed.TenantId;
            key.OwnerType = changed.OwnerType;
            key.OwnerId = changed.OwnerId;
        }
        await context.SaveChangesAsync();
        return changed;
    }
}
