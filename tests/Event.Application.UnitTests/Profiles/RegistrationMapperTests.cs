using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRegistrationPolicies.Handlers.Queries;
using Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;
using Explore.Application.Features.EventSessionKinds.Handlers.Queries;
using Explore.Application.Features.EventSessionKinds.Requests.Queries;
using Explore.Application.Features.RegistrationModes.Handlers.Queries;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using Explore.Application.Features.RegistrationScopes.Handlers.Queries;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using Explore.Application.Features.ScheduleItemKinds.Handlers.Queries;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public class RegistrationMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task ScopeList_PreservesJsonAndSnapshot(string? description)
    {
        var items = new List<RegistrationScope>
        {
            new() { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description },
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second", Description = null }
        };
        var handler = new GetRegistrationScopeListRequestHandler(new ScopeStore(items));
        var result = await handler.QueryAsync(new GetRegistrationScopeListRequest(), CancellationToken.None);
        items[0].FullName = "Changed";
        items[0].Description = "Changed";
        items.Clear();
        await AssertList(result, description);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task PolicyList_PreservesJsonAndSnapshot(string? description)
    {
        var items = new List<EventRegistrationPolicy>
        {
            new() { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description },
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second", Description = null }
        };
        var handler = new GetEventRegistrationPolicyListRequestHandler(new PolicyStore(items));
        var result = await handler.Handle(new GetEventRegistrationPolicyListRequest(), CancellationToken.None);
        items[0].FullName = "Changed";
        items[0].Description = "Changed";
        items.Clear();
        await AssertList(result, description);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task SessionKindList_PreservesJsonAndSnapshot(string? description)
    {
        var items = new List<EventSessionKind>
        {
            new() { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description },
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second", Description = null }
        };
        var handler = new GetEventSessionKindListRequestHandler(new SessionKindStore(items));
        var result = await handler.Handle(new GetEventSessionKindListRequest(), CancellationToken.None);
        items[0].FullName = "Changed";
        items[0].Description = "Changed";
        items.Clear();
        await AssertList(result, description);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task ScheduleKindList_PreservesJsonAndSnapshot(string? description)
    {
        var items = new List<ScheduleItemKind>
        {
            new() { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description },
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second", Description = null }
        };
        var handler = new GetScheduleItemKindListRequestHandler(new ScheduleKindStore(items));
        var result = await handler.Handle(new GetScheduleItemKindListRequest(), CancellationToken.None);
        items[0].FullName = "Changed";
        items[0].Description = "Changed";
        items.Clear();
        await AssertList(result, description);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task ModeList_PreservesJsonAndSnapshot(string? description)
    {
        var items = new List<RegistrationMode>
        {
            new() { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description },
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second", Description = null }
        };
        var handler = new GetRegistrationModeListRequestHandler(new ModeStore(items));
        var result = await handler.QueryAsync(new GetRegistrationModeListRequest(), CancellationToken.None);
        items[0].FullName = "Changed";
        items[0].Description = "Changed";
        items.Clear();
        await AssertList(result, description);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Registration description")]
    public async Task ModeDetail_SelectsRequestedEntityAndPreservesJson(string? description)
    {
        var selected = new RegistrationMode { Id = 7, MasterCode = "FIRST", FullName = "First", Description = description };
        var handler = new GetRegistrationModeDetailsRequestHandler(new ModeStore([
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second" }, selected]));

        var result = await handler.QueryAsync(new GetRegistrationModeDetailsRequest(7), CancellationToken.None);
        selected.FullName = "Changed";
        selected.Description = "Changed";

        var expected = JsonNode.Parse("""{"id":7,"masterCode":"FIRST","fullName":"First","description":null}""")!;
        expected["description"] = description;
        await AssertJson(result, expected);
    }

    [Test]
    public async Task ModeDetail_MissingEntity_RemainsNull()
    {
        var handler = new GetRegistrationModeDetailsRequestHandler(new ModeStore([
            new() { Id = 2, MasterCode = "SECOND", FullName = "Second" }]));

        var result = await handler.QueryAsync(new GetRegistrationModeDetailsRequest(7), CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task EmptyRepositories_ReturnEmptyJsonArrays()
    {
        await AssertJson(await new GetRegistrationScopeListRequestHandler(new ScopeStore([]))
            .QueryAsync(new GetRegistrationScopeListRequest(), CancellationToken.None), new JsonArray());
        await AssertJson(await new GetEventRegistrationPolicyListRequestHandler(new PolicyStore([]))
            .Handle(new GetEventRegistrationPolicyListRequest(), CancellationToken.None), new JsonArray());
        await AssertJson(await new GetEventSessionKindListRequestHandler(new SessionKindStore([]))
            .Handle(new GetEventSessionKindListRequest(), CancellationToken.None), new JsonArray());
        await AssertJson(await new GetScheduleItemKindListRequestHandler(new ScheduleKindStore([]))
            .Handle(new GetScheduleItemKindListRequest(), CancellationToken.None), new JsonArray());
        await AssertJson(await new GetRegistrationModeListRequestHandler(new ModeStore([]))
            .QueryAsync(new GetRegistrationModeListRequest(), CancellationToken.None), new JsonArray());
    }

    private static async Task AssertList(object result, string? description)
    {
        var expected = JsonNode.Parse("""
            [{"id":7,"masterCode":"FIRST","fullName":"First","description":null},
             {"id":2,"masterCode":"SECOND","fullName":"Second","description":null}]
            """)!;
        expected[0]!["description"] = description;
        await AssertJson(result, expected);
    }

    private static async Task AssertJson(object? result, JsonNode expected)
    {
        var actual = JsonSerializer.SerializeToNode(result, JsonOptions);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue()
            .Because($"Expected {expected}; actual {actual}");
    }

    private abstract class Store<T>(List<T> items, Func<T, int> key) : IGenericRepository<T, int> where T : class
    {
        public Task<IReadOnlyList<T>> GetAll() => Task.FromResult<IReadOnlyList<T>>(items);
        public Task<T?> GetById(int id) => Task.FromResult(items.SingleOrDefault(item => key(item) == id));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(int id) => throw new NotSupportedException();
        public Task<T> Create(T entity) => throw new NotSupportedException();
        public Task Update(T entity) => throw new NotSupportedException();
        public Task Delete(T entity) => throw new NotSupportedException();
    }

    private sealed class ScopeStore(List<RegistrationScope> items) : Store<RegistrationScope>(items, item => item.Id), IRegistrationScopeRepository;
    private sealed class PolicyStore(List<EventRegistrationPolicy> items) : Store<EventRegistrationPolicy>(items, item => item.Id), IEventRegistrationPolicyRepository;
    private sealed class SessionKindStore(List<EventSessionKind> items) : Store<EventSessionKind>(items, item => item.Id), IEventSessionKindRepository;
    private sealed class ScheduleKindStore(List<ScheduleItemKind> items) : Store<ScheduleItemKind>(items, item => item.Id), IScheduleItemKindRepository;
    private sealed class ModeStore(List<RegistrationMode> items) : Store<RegistrationMode>(items, item => item.Id), IRegistrationModeRepository;
}
