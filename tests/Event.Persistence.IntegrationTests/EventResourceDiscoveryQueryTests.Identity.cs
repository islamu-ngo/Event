using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain.Enums;
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
        var original = new ApiKeyPrincipalContext("resource-consumer-one", scope.TenantAId,
            ExternalApiKeyOwnerType.User, user, []);
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

        machine.Current.Returns(ChangedPrincipal(original, coordinate));
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
        var original = new ApiKeyPrincipalContext("resource-consumer-one", scope.TenantAId,
            ExternalApiKeyOwnerType.User, user, []);
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(original);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, machinePrincipal: machine);
        var accepted = await workflow.GetAsync(resource.Id, default);
        await Assert.That(accepted.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        bool delivered = false;
        var outcome = await accepted.Projection!.DiscloseAsync((value, _) =>
        {
            machine.Current.Returns(ChangedPrincipal(original, coordinate));
            return Task.FromResult(value);
        }, (_, _) =>
        {
            delivered = true;
            return Task.CompletedTask;
        });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    private static ApiKeyPrincipalContext ChangedPrincipal(ApiKeyPrincipalContext original, string coordinate) =>
        coordinate switch
        {
            "key" => original with { KeyId = "resource-consumer-two" },
            "owner-kind" => original with { OwnerType = ExternalApiKeyOwnerType.Organization },
            "owner" => original with { OwnerId = Guid.CreateVersion7() },
            "authority-tenant" => original with { TenantId = Guid.CreateVersion7() },
            _ => throw new ArgumentOutOfRangeException(nameof(coordinate))
        };
}
