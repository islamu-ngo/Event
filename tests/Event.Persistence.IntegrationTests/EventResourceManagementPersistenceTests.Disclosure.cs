using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    public async Task ManagementPageCannotReturnMetadataChangedDuringItsProviderCheck()
    {
        var (scope, actor) = await SeedAsync();
        var id = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            await using var creation = Workflow(seed, scope.TenantAId, actor);
            await creation.InitializeAsync();
            await creation.PrepareAsync();
            await Assert.That((await creation.Workflow
                .CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        }
        bool changed = false;
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var inputs = call.Arg<IReadOnlyList<EventResourceProviderInput>>();
                if (!changed && inputs.Any(input => input.Resource.Id == id))
                {
                    changed = true;
                    await using var writer = database.CreateIndependentContext();
                    var resource = await writer.EventResources.SingleAsync(row => row.Id == id);
                    resource.UpdateMetadata(new EventResourceMetadata
                    {
                        Title = "Changed after projection",
                        Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                    }, resource.ConcurrencyStamp, actor, Now);
                    await writer.SaveChangesAsync();
                }
                return (IReadOnlyList<EventResourceProviderDecision>)inputs.Select(_ => EventResourceProviderDecision.Allow).ToArray();
            });
        await using var context = database.CreateIndependentContext();
        await using var request = Workflow(context, scope.TenantAId, actor, provider: provider);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var result = await request.Workflow
            .ListAsync(scope.EventAId, 1, 20, default);
        await Assert.That(changed).IsTrue();
        await Assert.That(result.Projection).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
    }

    [Test]
    public async Task ParentManagementAllowCannotDiscloseAResourceDeniedByTheSelectedProvider()
    {
        var (scope, actor) = await SeedAsync();
        var id = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            await using var creation = Workflow(seed, scope.TenantAId, actor);
            await creation.InitializeAsync();
            await creation.PrepareAsync();
            await Assert.That((await creation.Workflow
                .CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        }
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<EventResourceProviderInput>().Resource.Id == scope.EventAId
                ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny);
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<EventResourceProviderInput>>().Select(input =>
                input.Resource.Id == scope.EventAId ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny).ToArray());
        await using var context = database.CreateContext();
        await using var request = Workflow(context, scope.TenantAId, actor, provider: provider);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var result = await request.Workflow
            .ListAsync(scope.EventAId, 1, 20, default);
        await Assert.That(result.Projection).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
    }
}
