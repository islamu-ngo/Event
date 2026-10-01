using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Categories.Handlers.Queries;
using Explore.Application.Features.Categories.Requests.Queries;
using Explore.Application.Features.CategoryTypes.Handlers.Queries;
using Explore.Application.Features.CategoryTypes.Requests.Queries;
using Explore.Application.Features.Tags.Handlers.Queries;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Features.TagTypes.Handlers.Queries;
using Explore.Application.Features.TagTypes.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class NullableDetailRepositoryTests
{
    [Test]
    public async Task MissingRows_ReturnNoEntityAndNoDetailDto()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
        await using var context = new ExploreDbContext(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options)
        {
            TenantContext = new FixedTenantContext(Guid.CreateVersion7())
        };
        await context.Database.EnsureCreatedAsync();
        Guid missingAggregateId = Guid.CreateVersion7();
        const int missingLookupId = int.MaxValue;
        ICategoryRepository categories = new CategoryRepository(context);
        ICategoryTypeRepository categoryTypes = new CategoryTypeRepository(context);
        ITagRepository tags = new TagRepository(context);
        ITagTypeRepository tagTypes = new TagTypeRepository(context);

        await Assert.That(await new ApprovalStatusRepository(context).GetStatusTypeWithDetails(missingLookupId)).IsNull();
        await Assert.That(await new AudienceAgeRepository(context).GetAudienceAgeWithDetails(missingLookupId)).IsNull();
        await Assert.That(await new AudienceGenderRepository(context).GetAudienceGenderWithDetails(missingLookupId)).IsNull();
        await Assert.That(await new EventTypeRepository(context).GetEventTypeWithDetails(missingLookupId)).IsNull();
        await Assert.That(await categories.GetCategoryWithDetails(missingAggregateId)).IsNull();
        await Assert.That(await categoryTypes.GetCategoryTypeWithDetails(missingLookupId)).IsNull();
        await Assert.That(await tags.GetTagWithDetails(missingAggregateId)).IsNull();
        await Assert.That(await tagTypes.GetTagTypeWithDetails(missingLookupId)).IsNull();

        await Assert.That(await new GetCategoryDetailsRequestHandler(categories)
            .QueryAsync(new GetCategoryDetailsRequest { Id = missingAggregateId }, default)).IsNull();
        await Assert.That(await new GetCategoryTypeDetailsRequestHandler(categoryTypes)
            .QueryAsync(new GetCategoryTypeDetailsRequest { Id = missingLookupId }, default)).IsNull();
        await Assert.That(await new GetTagDetailsRequestHandler(tags)
            .QueryAsync(new GetTagDetailsRequest { Id = missingAggregateId }, default)).IsNull();
        await Assert.That(await new GetTagTypeDetailsRequestHandler(tagTypes)
            .QueryAsync(new GetTagTypeDetailsRequest { Id = missingLookupId }, default)).IsNull();
    }

    private sealed record FixedTenantContext(Guid TenantId) : ITenantContext;
}
