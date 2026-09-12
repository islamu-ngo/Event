using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeCategoryTypeCategoriesTests
{
    private const int AlphaTypeId = 8108;
    private const int ZetaTypeId = 8102;
    private const int UnusedTypeId = 8110;

    [Test]
    public async Task NativeMutations_ValidateTargetsAndDuplicatesBeforeChangingTenantBoundRelationships()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .SetTenant(PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var create = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<CreateCategoryTypeCategoriesCommand, BaseCommandResponse<Guid>>>();
            var update = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<UpdateCategoryTypeCategoriesCommand, BaseCommandResponse<Guid>>>();
            var delete = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<DeleteCategoryTypeCategoriesCommand, bool>>();

            CreateCategoryTypeCategoriesDto[] invalidCreates =
            [
                new() { CategoryId = Guid.Empty, CategoryTypeId = AlphaTypeId },
                new() { CategoryId = Guid.NewGuid(), CategoryTypeId = AlphaTypeId },
                new() { CategoryId = data.Gamma.Id, CategoryTypeId = 0 },
                new() { CategoryId = data.Gamma.Id, CategoryTypeId = int.MaxValue },
                new() { CategoryId = data.Foreign.Id, CategoryTypeId = ZetaTypeId },
                new() { CategoryId = data.Alpha.Id, CategoryTypeId = AlphaTypeId }
            ];
            foreach (var dto in invalidCreates)
            {
                var rejected = await create.ExecuteAsync(new CreateCategoryTypeCategoriesCommand
                {
                    CategoryTypeCategoriesDto = dto
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(rejected.Errors).IsNotNull();
                await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(3);
            }

            var created = await create.ExecuteAsync(new CreateCategoryTypeCategoriesCommand
            {
                CategoryTypeCategoriesDto = new()
                {
                    CategoryId = data.Gamma.Id,
                    CategoryTypeId = AlphaTypeId,
                    TenantId = data.OtherTenantId
                }
            }, CancellationToken.None);
            await Assert.That(created.IsSuccess).IsTrue();
            await Assert.That(created.Id).IsNotEqualTo(Guid.Empty);
            await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
            await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(4);
            var wireResult = JsonSerializer.SerializeToElement(created, JsonSerializerOptions.Web);
            await Assert.That(wireResult.GetProperty("success").GetBoolean()).IsTrue();

            UpdateCategoryTypeCategoriesDto[] invalidUpdates =
            [
                new(),
                new() { Relationship = new() },
                new() { Relationship = new() { CategoryId = Guid.Empty } },
                new() { Relationship = new() { CategoryId = Guid.NewGuid() } },
                new() { Relationship = new() { CategoryTypeId = 0 } },
                new() { Relationship = new() { CategoryTypeId = int.MaxValue } },
                new() { Relationship = new() { CategoryId = data.Foreign.Id } },
                new() { Relationship = new() { CategoryId = data.Alpha.Id } }
            ];
            foreach (var dto in invalidUpdates)
            {
                var rejected = await update.ExecuteAsync(new UpdateCategoryTypeCategoriesCommand
                {
                    CategoryTypeCategoriesId = created.Id,
                    CategoryTypeCategoriesDto = dto
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(rejected.Errors).IsNotNull();
                await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
                await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(4);
            }

            foreach (var inaccessibleId in new[] { Guid.NewGuid(), data.ForeignLinkId })
            {
                var rejected = await update.ExecuteAsync(new UpdateCategoryTypeCategoriesCommand
                {
                    CategoryTypeCategoriesId = inaccessibleId,
                    CategoryTypeCategoriesDto = new()
                    {
                        Relationship = new() { CategoryId = data.Beta.Id }
                    }
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(await delete.ExecuteAsync(
                    new DeleteCategoryTypeCategoriesCommand(inaccessibleId), CancellationToken.None)).IsFalse();
                await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
                await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(4);
            }

            var unchanged = await update.ExecuteAsync(new UpdateCategoryTypeCategoriesCommand
            {
                CategoryTypeCategoriesId = created.Id,
                CategoryTypeCategoriesDto = new()
                {
                    Relationship = new() { CategoryTypeId = AlphaTypeId }
                }
            }, CancellationToken.None);
            await Assert.That(unchanged.IsSuccess).IsTrue();
            await Assert.That(unchanged.Id).IsEqualTo(created.Id);
            await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);

            var movedCategory = await update.ExecuteAsync(new UpdateCategoryTypeCategoriesCommand
            {
                CategoryTypeCategoriesId = created.Id,
                CategoryTypeCategoriesDto = new()
                {
                    Relationship = new() { CategoryId = data.Beta.Id, CategoryTypeId = ZetaTypeId }
                }
            }, CancellationToken.None);
            await Assert.That(movedCategory.IsSuccess).IsTrue();
            await Assert.That(movedCategory.Id).IsEqualTo(created.Id);
            await AssertRelationshipAsync(db, created.Id, data.Beta.Id, ZetaTypeId);
            await AssertRelationshipAsync(db, data.AlphaLinkId, data.Alpha.Id, AlphaTypeId);

            await Assert.That(await delete.ExecuteAsync(
                new DeleteCategoryTypeCategoriesCommand(created.Id), CancellationToken.None)).IsTrue();
            await Assert.That(await db.CategoryTypeCategories.AnyAsync(link => link.Id == created.Id)).IsFalse();
            await Assert.That(await delete.ExecuteAsync(
                new DeleteCategoryTypeCategoriesCommand(created.Id), CancellationToken.None)).IsFalse();
            await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(3);
        }

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.OtherTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var foreign = await db.CategoryTypeCategories.AsNoTracking().SingleAsync();
            await Assert.That(foreign.Id).IsEqualTo(data.ForeignLinkId);
            await Assert.That(foreign.CategoryId).IsEqualTo(data.Foreign.Id);
            await Assert.That(foreign.CategoryTypeId).IsEqualTo(AlphaTypeId);
            await Assert.That(foreign.TenantId).IsEqualTo(data.OtherTenantId);
        }
    }

    [Test]
    public async Task NativeReads_UseTenantFilteredRepositoriesAndReturnNullableMissingDetails()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var detail = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetCategoryTypeCategoriesDetailsRequest, CategoryTypeCategoriesDto?>>();
        var list = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetCategoryTypeCategoriesListRequest, List<CategoryTypeCategoriesListDto>>>();
        var categories = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetCategoriesByCategoryTypeRequest, List<CategoryListDto>>>();
        var types = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetCategoryTypesForCategoryRequest, List<CategoryTypeListDto>>>();
        var grouped = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetCategoriesGroupedByCategoryTypeRequest, List<CategoryTypeWithCategoriesDto>>>();

        var found = await detail.QueryAsync(
            new GetCategoryTypeCategoriesDetailsRequest(data.AlphaLinkId), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the current tenant's relationship.");
        await Assert.That(found.Id).IsEqualTo(data.AlphaLinkId);
        await Assert.That(found.CategoryId).IsEqualTo(data.Alpha.Id);
        await Assert.That(found.CategoryTypeId).IsEqualTo(AlphaTypeId);
        await Assert.That(found.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(await detail.QueryAsync(
            new GetCategoryTypeCategoriesDetailsRequest(Guid.NewGuid()), CancellationToken.None)).IsNull();
        await Assert.That(await detail.QueryAsync(
            new GetCategoryTypeCategoriesDetailsRequest(data.ForeignLinkId), CancellationToken.None)).IsNull();

        var links = await list.QueryAsync(new GetCategoryTypeCategoriesListRequest(), CancellationToken.None);
        await Assert.That(links.Select(link => link.CategoryId))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id, data.Gamma.Id });
        var byType = await categories.QueryAsync(
            new GetCategoriesByCategoryTypeRequest(AlphaTypeId), CancellationToken.None);
        await Assert.That(byType.Select(category => category.Id))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id });
        await Assert.That(await categories.QueryAsync(
            new GetCategoriesByCategoryTypeRequest(UnusedTypeId), CancellationToken.None)).IsEmpty();
        var forCategory = await types.QueryAsync(
            new GetCategoryTypesForCategoryRequest(data.Alpha.Id), CancellationToken.None);
        await Assert.That(forCategory).IsEquivalentTo(new[]
        {
            new CategoryTypeListDto { Id = AlphaTypeId, MasterCode = "ALPHA_TYPE", FullName = "Alpha type" }
        });
        await Assert.That(await types.QueryAsync(
            new GetCategoryTypesForCategoryRequest(data.Foreign.Id), CancellationToken.None)).IsEmpty();
        var groups = await grouped.QueryAsync(new GetCategoriesGroupedByCategoryTypeRequest(), CancellationToken.None);
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Id).IsEqualTo((int?)AlphaTypeId);
        await Assert.That(groups[1].Id).IsEqualTo((int?)ZetaTypeId);
        await Assert.That(groups.SelectMany(group => group.Categories).Select(category => category.Id))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id, data.Gamma.Id });
        await Assert.That(await db.CategoryTypeCategories.CountAsync()).IsEqualTo(3);
        await Assert.That(db.ChangeTracker.Entries()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();

    }

    [Test]
    public async Task AnonymousGroupedHttp_ReturnsOrderedTenantProjectionWithoutUnlinkedTypes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/categorytype/with-categories");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var groups = await response.Content.ReadFromJsonAsync<List<CategoryTypeWithCategoriesDto>>()
            ?? throw new InvalidOperationException("Expected a grouped category array.");

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Id).IsEqualTo((int?)AlphaTypeId);
        await Assert.That(groups[0].FullName).IsEqualTo("Alpha type");
        await Assert.That(groups[0].Description).IsEqualTo("Alpha description");
        await Assert.That(groups[0].Categories.Count).IsEqualTo(2);
        await Assert.That(groups[0].Categories[0]).IsEqualTo(new CategoryListDto
        {
            Id = data.Alpha.Id,
            ConcurrencyStamp = data.Alpha.ConcurrencyStamp,
            MasterCode = "ALPHA",
            FullName = "Alpha category"
        });
        await Assert.That(groups[0].Categories[1]).IsEqualTo(new CategoryListDto
        {
            Id = data.Beta.Id,
            ConcurrencyStamp = data.Beta.ConcurrencyStamp,
            MasterCode = "BETA",
            FullName = "Beta category"
        });
        await Assert.That(groups[1].Id).IsEqualTo((int?)ZetaTypeId);
        await Assert.That(groups[1].FullName).IsEqualTo("Zeta type");
        await Assert.That(groups[1].Description).IsNull();
        await Assert.That(groups[1].Categories.Single()).IsEqualTo(new CategoryListDto
        {
            Id = data.Gamma.Id,
            ConcurrencyStamp = data.Gamma.ConcurrencyStamp,
            MasterCode = "GAMMA",
            FullName = "Gamma category"
        });
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
        foreach (var group in payload.RootElement.EnumerateArray())
        {
            await Assert.That(group.TryGetProperty("tenantId", out _)).IsFalse();
            foreach (var category in group.GetProperty("categories").EnumerateArray())
            {
                await Assert.That(category.TryGetProperty("tenantId", out _)).IsFalse();
                await Assert.That(category.TryGetProperty("tenant", out _)).IsFalse();
            }
        }
    }

    [Test]
    public async Task AnonymousGroupedHttp_WithOnlyOtherTenantRelationships_ReturnsEmptyArray()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .SetTenant(PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.CategoryTypeCategories.RemoveRange(await db.CategoryTypeCategories.ToListAsync());
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/categorytype/with-categories");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(payload.RootElement.GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task GroupedHttpContract_PreservesRouteClassificationCacheAndResponseSchema()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty("/api/categorytype/with-categories").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString())
            .IsEqualTo("GetCategoryTypeOptionsWithCategories");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "CategoryType" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        var method = typeof(CategoryTypeController).GetMethod(nameof(CategoryTypeController.GetWithCategories))!;
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo("LookupData");

        var responseSchema = ResolveSchema(root, operation.GetProperty("responses")
            .GetProperty(((int)HttpStatusCode.OK).ToString(CultureInfo.InvariantCulture))
            .GetProperty("content").EnumerateObject()
            .Single(content => content.Name.Split(';')[0] == "application/json").Value.GetProperty("schema"));
        await Assert.That(responseSchema.GetProperty("type").GetString()).IsEqualTo("array");
        var groupSchema = ResolveSchema(root, responseSchema.GetProperty("items"));
        var properties = groupSchema.GetProperty("properties");
        await Assert.That(properties.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "id", "fullName", "description", "categories" });
        await Assert.That(properties.GetProperty("id").GetProperty("format").GetString()).IsEqualTo("int32");
        await Assert.That(properties.GetProperty("fullName").GetProperty("type").GetString()).IsEqualTo("string");
        var categories = ResolveSchema(root, properties.GetProperty("categories"));
        await Assert.That(categories.GetProperty("type").GetString()).IsEqualTo("array");
        var categoryProperties = ResolveSchema(root, categories.GetProperty("items")).GetProperty("properties");
        await Assert.That(categoryProperties.GetProperty("id").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(categoryProperties.GetProperty("concurrencyStamp").GetProperty("format").GetString())
            .IsEqualTo("uuid");
        await Assert.That(categoryProperties.TryGetProperty("tenantId", out _)).IsFalse();
    }

    private static JsonElement ResolveSchema(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!["#/components/schemas/".Length..];
        return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static async Task AssertRelationshipAsync(
        ExploreDbContext db, Guid id, Guid categoryId, int categoryTypeId)
    {
        var persisted = await db.CategoryTypeCategories.AsNoTracking().SingleAsync(link => link.Id == id);
        await Assert.That(persisted.CategoryId).IsEqualTo(categoryId);
        await Assert.That(persisted.CategoryTypeId).IsEqualTo(categoryTypeId);
        await Assert.That(persisted.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
    }

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            Slug = "native-category-links",
            FullName = "Native category links",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "other-category-links",
            FullName = "Other category links",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        db.Tenants.AddRange(tenant, otherTenant);
        var alphaType = new CategoryType
        {
            Id = AlphaTypeId,
            MasterCode = "ALPHA_TYPE",
            FullName = "Alpha type",
            Description = "Alpha description"
        };
        var zetaType = new CategoryType
        {
            Id = ZetaTypeId, MasterCode = "ZETA_TYPE", FullName = "Zeta type"
        };
        db.CategoryTypes.AddRange(zetaType, alphaType, new CategoryType
        {
            Id = UnusedTypeId, MasterCode = "UNUSED_TYPE", FullName = "Unused type"
        });
        var alpha = NewCategory(tenant, "ALPHA", "Alpha category");
        var beta = NewCategory(tenant, "BETA", "Beta category");
        var gamma = NewCategory(tenant, "GAMMA", "Gamma category");
        var foreign = NewCategory(otherTenant, "FOREIGN", "Foreign category");
        db.Categories.AddRange(gamma, beta, alpha, foreign);
        var alphaLink = NewLink(tenant, alpha, alphaType);
        var foreignLink = NewLink(otherTenant, foreign, alphaType);
        db.CategoryTypeCategories.AddRange(
            NewLink(tenant, gamma, zetaType),
            NewLink(tenant, beta, alphaType),
            alphaLink,
            foreignLink);
        await db.SaveChangesAsync();
        return new SeedData(otherTenant.Id, alphaLink.Id, foreignLink.Id, alpha, beta, gamma, foreign);
    }

    private static Category NewCategory(Tenant tenant, string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        MasterCode = code,
        FullName = name,
        TenantId = tenant.Id,
        Tenant = tenant
    };

    private static CategoryTypeCategories NewLink(Tenant tenant, Category category, CategoryType type) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.Id,
        Tenant = tenant,
        CategoryId = category.Id,
        Category = category,
        CategoryTypeId = type.Id,
        CategoryType = type
    };

    private sealed record SeedData(
        Guid OtherTenantId,
        Guid AlphaLinkId,
        Guid ForeignLinkId,
        Category Alpha,
        Category Beta,
        Category Gamma,
        Category Foreign);
}
