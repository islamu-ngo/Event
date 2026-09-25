using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Handlers.Commands;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    public async Task NativeCreateRejectsActualResourceFiveHundredAndOneWithoutWritingResourceOrAudit()
    {
        var (scope, actor) = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.AddRange(Enumerable.Range(0, 500)
                .Select(_ => EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId)));
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var id = Guid.CreateVersion7();
        var result = await new CreateEventResourceCommandHandler(Workflow(context, scope.TenantAId, actor))
            .ExecuteAsync(new(scope.EventAId, id, Draft()));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventResourceManagementFailureCodes.CapacityExceeded);
        await Assert.That(await new EventResourceRepository(context).CountActiveAsync(scope.TenantAId, scope.EventAId, default)).IsEqualTo(500);
        await Assert.That(await context.EventResources.AnyAsync(value => value.Id == id)).IsFalse();
        await Assert.That(await context.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }
}
