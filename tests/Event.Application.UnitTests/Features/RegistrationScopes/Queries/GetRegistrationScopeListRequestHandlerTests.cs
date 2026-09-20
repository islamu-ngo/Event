using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.RegistrationScopes.Handlers.Queries;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using Explore.Domain;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.RegistrationScopes.Queries;

public class GetRegistrationScopeListRequestHandlerTests
{
    [Test]
    public async Task QueryAsync_WithExistingScopes_ReturnsMappedList()
    {
        var scopes = new List<RegistrationScope>
        {
            new() { Id = 3, MasterCode = "SESSION_SELECTION", FullName = "Session Selection", Description = "Choose sessions" },
            new() { Id = 1, MasterCode = "EVENT", FullName = "Event", Description = null },
            new() { Id = 2, MasterCode = "DAY", FullName = "Day", Description = "" }
        };
        var handler = new GetRegistrationScopeListRequestHandler(new ScopeStore(scopes));

        var result = await handler.QueryAsync(new GetRegistrationScopeListRequest(), CancellationToken.None);
        scopes[0].FullName = "Changed";
        scopes[0].Description = "Changed";
        scopes.Clear();

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].Id).IsEqualTo(3);
        await Assert.That(result[0].MasterCode).IsEqualTo("SESSION_SELECTION");
        await Assert.That(result[0].FullName).IsEqualTo("Session Selection");
        await Assert.That(result[0].Description).IsEqualTo("Choose sessions");
        await Assert.That(result[1].Id).IsEqualTo(1);
        await Assert.That(result[1].MasterCode).IsEqualTo("EVENT");
        await Assert.That(result[1].FullName).IsEqualTo("Event");
        await Assert.That(result[1].Description).IsNull();
        await Assert.That(result[2].Id).IsEqualTo(2);
        await Assert.That(result[2].MasterCode).IsEqualTo("DAY");
        await Assert.That(result[2].FullName).IsEqualTo("Day");
        await Assert.That(result[2].Description).IsEqualTo("");
    }

    [Test]
    public async Task QueryAsync_WithNoScopes_ReturnsEmptyList()
    {
        var handler = new GetRegistrationScopeListRequestHandler(new ScopeStore([]));

        var result = await handler.QueryAsync(new GetRegistrationScopeListRequest(), CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(0);
    }

    private sealed class ScopeStore(List<RegistrationScope> items) : IRegistrationScopeRepository
    {
        public Task<IReadOnlyList<RegistrationScope>> GetAll() => Task.FromResult<IReadOnlyList<RegistrationScope>>(items);
        public Task<RegistrationScope?> GetById(int id) => Task.FromResult(items.SingleOrDefault(item => item.Id == id));
        public Task<(IReadOnlyList<RegistrationScope> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(int id) => throw new NotSupportedException();
        public Task<RegistrationScope> Create(RegistrationScope entity) => throw new NotSupportedException();
        public Task Update(RegistrationScope entity) => throw new NotSupportedException();
        public Task Delete(RegistrationScope entity) => throw new NotSupportedException();
    }
}
