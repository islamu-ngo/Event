using System.Data;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    public async Task OwnerFencePortablePreservesGraphCustodyAndRejectsInactivePlacement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
            .UseSnakeCaseNamingConvention();
        Seed seed;
        await using (var setup = new ExploreDbContext(options.Options))
        {
            setup.EnableTenantFilterBypass("Portable owner fence fixture seeding.");
            await setup.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(setup);
            seed = await SeedRowsAsync(setup);
        }
        await using var context = new ExploreDbContext(options.Options) { TenantContext = new SelectedTenant(seed.Tenant) };
        var repository = new AiConversationRepository(context);
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = seed.Tenant,
            UserId = seed.User,
            ActorId = seed.GroupActor,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await repository.Create(conversation);
            var held = await repository.GetByIdForUpdateAsync(conversation.Id, seed.GroupActor, default);
            await Assert.That(held).IsNotNull();
            held!.AddMessage(AiMessageRole.Assistant, "Portable result", null, DateTime.UtcNow);
            await repository.Update(held);
            await owner.CommitAsync();
        }
        await context.GroupTenants.Where(row => row.Id == seed.GroupPlacement)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ApprovalStatusId, (int)ApprovalStatusEnum.Pending));
        await using var rejected = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await repository.GetByIdForUpdateAsync(conversation.Id, seed.GroupActor, default)).IsNull();
        await rejected.RollbackAsync();
    }
}
