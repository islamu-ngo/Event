using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventCustomPropertyNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyOwnedEvent_CreateInvalidatesEveryWarmPage_OnlyAfterCommit(bool failCommit)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        Guid emptyEventId;
        using (var seedScope = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var source = await db.Events.SingleAsync(row => row.Id == data.EventId);
            var emptyEvent = await EventScenarioSeed.SeedPublishedEventAsync(db, source.ActorId, PlatformDefaults.DefaultTenantId);
            emptyEventId = emptyEvent.EventId;
            var owned = await seedScope.ServiceProvider.GetRequiredService<IEventRepository>().GetById(emptyEventId);
            await Assert.That(owned!.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        }

        (int Page, int Size)[] variants = [(1, 1), (2, 1), (1, 2)];
        foreach (var (page, size) in variants)
        {
            var before = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
            var empty = await ListAsync(factory, PlatformDefaults.DefaultTenantId, emptyEventId, page, size);
            await Assert.That(empty.TotalCount).IsEqualTo(0);
            await Assert.That(empty.Items.Count).IsEqualTo(0);
            await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsGreaterThan(before);
            var foreignBefore = factory.Reads.Count(data.ForeignTenantId);
            await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, page, size);
            await Assert.That(factory.Reads.Count(data.ForeignTenantId)).IsGreaterThan(foreignBefore);
        }
        var ownWarmReads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        var foreignWarmReads = factory.Reads.Count(data.ForeignTenantId);
        foreach (var (page, size) in variants)
        {
            var emptyHit = await ListAsync(factory, PlatformDefaults.DefaultTenantId, emptyEventId, page, size);
            await Assert.That(emptyHit.TotalCount).IsEqualTo(0);
            await Assert.That(emptyHit.Items.Count).IsEqualTo(0);
            await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(ownWarmReads);
            await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, page, size);
            await Assert.That(factory.Reads.Count(data.ForeignTenantId)).IsEqualTo(foreignWarmReads);
        }

        HttpResponseMessage response;
        factory.Commits.Fail = failCommit;
        try
        {
            response = await client.PostAsJsonAsync(Root, CreateDto(emptyEventId));
        }
        finally
        {
            factory.Commits.Fail = false;
        }
        using (response)
        {
            await Assert.That(response.StatusCode).IsEqualTo(failCommit ? HttpStatusCode.InternalServerError : HttpStatusCode.Created);
            if (failCommit) await ProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
            var createdId = failCommit ? Guid.Empty : (await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id;
            foreach (var (page, size) in variants)
            {
                var before = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
                var current = await ListAsync(factory, PlatformDefaults.DefaultTenantId, emptyEventId, page, size);
                await Assert.That(current.TotalCount).IsEqualTo(failCommit ? 0 : 1);
                await Assert.That(current.Items.Count).IsEqualTo(!failCommit && page == 1 ? 1 : 0);
                if (failCommit)
                    await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(before);
                else
                {
                    await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsGreaterThan(before);
                    if (page == 1)
                    {
                        await Assert.That(current.Items.Single().Id).IsEqualTo(createdId);
                        await Assert.That(current.Items.Single().DisplayName).IsEqualTo("Created");
                    }
                }
                var foreign = await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, page, size);
                await Assert.That(foreign.TotalCount).IsEqualTo(1);
                await Assert.That(foreign.Items.Count).IsEqualTo(page == 1 ? 1 : 0);
                if (page == 1) await Assert.That(foreign.Items.Single().Id).IsEqualTo(data.ForeignDefinitionId);
                await Assert.That(factory.Reads.Count(data.ForeignTenantId)).IsEqualTo(foreignWarmReads);
            }
            using var verifyScope = Scope(factory, PlatformDefaults.DefaultTenantId);
            var persisted = await verifyScope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionsForEventPaged(emptyEventId, 1, 20);
            await Assert.That(persisted.TotalCount).IsEqualTo(failCommit ? 0 : 1);
            if (!failCommit) await Assert.That(persisted.Items.Single().Id).IsEqualTo(createdId);
        }
    }

    [Test]
    public async Task PendingCommit_KeepsCommittedPagesVisibleUntilRelease()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1);
        var before = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        factory.Commits.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Commits.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = factory.Commits.Entered.Task;
        var mutation = MutateAsync(client, data, "create");
        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1)).TotalCount).IsEqualTo(3);
            // Create performs database validation reads; the read below must remain a cache hit.
            var reads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
            await Assert.That(reads).IsGreaterThan(before);
            await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1);
            await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(reads);
        }
        finally
        {
            factory.Commits.Release.TrySetResult();
        }
        using var response = await mutation.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1)).TotalCount).IsEqualTo(4);
    }

    [Test]
    public async Task RejectedWritesAndForeignDeletePurge_PreserveStateAndExactErrors()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1);
        using var stale = await MutateAsync(client, data with { Stamp = Guid.CreateVersion7() }, "update");
        await ProblemAsync(stale, HttpStatusCode.Conflict, "concurrent_update");
        using var duplicate = await client.PostAsJsonAsync(Root, CreateDto(data.EventId) with { Key = "own" });
        await ProblemAsync(duplicate, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyDefinition");
        using var foreignUpdate = await MutateAsync(client, data with { DefinitionId = data.ForeignDefinitionId }, "update");
        await ProblemAsync(foreignUpdate, HttpStatusCode.Forbidden, "forbidden");
        using var foreignDelete = await client.DeleteAsync($"{Root}/{data.ForeignDefinitionId}");
        await Assert.That(foreignDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var foreignPurge = await MutateAsync(client, data with { DefinitionId = data.ForeignDefinitionId }, "purge");
        await ProblemAsync(foreignPurge, HttpStatusCode.NotFound, "resource_not_found");
        var reads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1)).TotalCount).IsEqualTo(3);
        await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(reads);
        await Assert.That((await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId)).DisplayName).IsEqualTo("own");
        await Assert.That((await DetailAsync(factory, data.ForeignTenantId, data.ForeignDefinitionId)).DisplayName).IsEqualTo("foreign-internal");
    }

    [Test]
    [Arguments("create", false)]
    [Arguments("update", false)]
    [Arguments("options", false)]
    [Arguments("delete", false)]
    [Arguments("purge", false)]
    [Arguments("create", true)]
    [Arguments("update", true)]
    [Arguments("options", true)]
    [Arguments("delete", true)]
    [Arguments("purge", true)]
    public async Task Mutation_CommitsOrRollsBack_AndOnlyThenInvalidatesAllTenantPages(string mutation, bool failCommit)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        foreach (var (page, size) in new[] { (1, 1), (2, 1), (1, 2) })
        {
            await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, page, size);
            await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, page, size);
        }
        var ownReads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        var foreignReads = factory.Reads.Count(data.ForeignTenantId);
        await Assert.That(ownReads).IsGreaterThan(0);
        await Assert.That(foreignReads).IsGreaterThan(0);
        factory.Commits.Fail = failCommit;
        using var response = await MutateAsync(client, data, mutation);
        factory.Commits.Fail = false;
        await Assert.That(response.StatusCode).IsEqualTo(failCommit ? HttpStatusCode.InternalServerError :
            mutation == "create" ? HttpStatusCode.Created : mutation == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.OK);
        var readsAfterMutation = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        var removed = !failCommit && mutation is "delete" or "purge";
        var total = failCommit ? 3 : mutation == "create" ? 4 : removed ? 2 : 3;
        foreach (var (page, size) in new[] { (1, 1), (2, 1), (1, 2) })
        {
            var own = await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, page, size);
            await Assert.That(own.TotalCount).IsEqualTo(total);
            if (page == 1)
            {
                await Assert.That(own.Items.First().Id).IsEqualTo(removed ? data.SecondId : data.DefinitionId);
                await Assert.That(own.Items.First().DisplayName).IsEqualTo(removed ? "second" : !failCommit && mutation == "update" ? "Updated" : "own");
                await Assert.That(own.Items.First().OptionCount).IsEqualTo(!failCommit && mutation == "options" ? 1 : 0);
            }
            var foreign = await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, page, size);
            await Assert.That(foreign.TotalCount).IsEqualTo(1);
            if (page == 1) await Assert.That(foreign.Items.Single().Id).IsEqualTo(data.ForeignDefinitionId);
        }
        await Assert.That(factory.Reads.Count(data.ForeignTenantId)).IsEqualTo(foreignReads);
        if (failCommit) await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(readsAfterMutation);
        else await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsGreaterThan(readsAfterMutation);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>();
        await Assert.That((await repository.GetDefinitionsForEventPaged(data.EventId, 1, 20)).TotalCount).IsEqualTo(total);
        var dependencies = await repository.GetPurgeDependencies(data.DefinitionId, default);
        var audits = await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetAll();
        if (!failCommit && mutation == "purge")
        {
            await Assert.That(dependencies).IsNull();
            var purged = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<CustomPropertyPurgeResultDto>>())!.Id!;
            await Assert.That(audits.Single().Id).IsEqualTo(purged.AuditLogId!.Value);
        }
        else
        {
            await Assert.That(dependencies).IsNotNull();
            await Assert.That(audits.Count).IsEqualTo(0);
            if (!removed)
            {
                var detail = await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId);
                await Assert.That(detail.DisplayName).IsEqualTo(!failCommit && mutation == "update" ? "Updated" : "own");
                await Assert.That(detail.Options.Count).IsEqualTo(!failCommit && mutation == "options" ? 1 : 0);
            }
        }
    }

    [Test]
    public async Task PostCommitCancellation_DoesNotSkipPageInvalidation()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1);
        using var cancellation = new CancellationTokenSource();
        factory.Commits.AfterCommit = cancellation.Cancel;
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId, data.AdminId);
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { DefinitionDto = CreateDto(data.EventId) }, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1)).TotalCount).IsEqualTo(4);
    }

    [Test]
    [Arguments("values")]
    [Arguments("audit")]
    [Arguments("provenance")]
    public async Task PurgeDependencies_BlockWithoutEvictionOrNewAudit(string blocker)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        if (blocker == "values")
        {
            using var set = await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "Retained"));
            await Assert.That(set.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        else
        {
            using var seedScope = Scope(factory, PlatformDefaults.DefaultTenantId);
            var db = seedScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (blocker == "audit") db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                EntityType = nameof(EventCustomPropertyDefinition),
                EntityId = data.DefinitionId.ToString(),
                Action = "RetainedReference",
                Timestamp = DateTime.UtcNow
            });
            else (await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId)).SourceTemplateDefinitionId = Guid.CreateVersion7();
            await db.SaveChangesAsync();
        }
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1);
        using var response = await MutateAsync(client, data, "purge");
        await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyDefinition");
        var reads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 2, 1)).TotalCount).IsEqualTo(3);
        await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(reads);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var dependencies = await scope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>().GetPurgeDependencies(data.DefinitionId, default);
        await Assert.That(dependencies!.HasBlockingDependencies).IsTrue();
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetAll()).Count).IsEqualTo(blocker == "audit" ? 1 : 0);
    }

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient client, SeedData data, string mutation)
    {
        if (mutation == "create") return await client.PostAsJsonAsync(Root, CreateDto(data.EventId));
        if (mutation == "delete") return await client.DeleteAsync($"{Root}/{data.DefinitionId}");
        if (mutation == "purge")
        {
            using var purge = new HttpRequestMessage(HttpMethod.Delete, $"{Root}/{data.DefinitionId}/purge")
            {
                Content = JsonContent.Create(new PurgeCustomPropertyDefinitionDto("Disposable event definition"))
            };
            return await client.SendAsync(purge);
        }
        var patch = mutation == "update"
            ? new UpdateEventCustomPropertyDefinitionDto { Metadata = new() { DisplayName = "Updated" } }
            : new UpdateEventCustomPropertyDefinitionDto
            {
                Validation = new() { PropertyType = Explore.Domain.Enums.PropertyType.Option },
                Options = new() { Items = [new() { Namespace = "tenant.community", Key = "kept", DisplayName = "Kept", Value = "kept", IsActive = true }] }
            };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.DefinitionId}") { Content = JsonContent.Create(patch) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{data.Stamp:D}\"");
        return await client.SendAsync(request);
    }
}
