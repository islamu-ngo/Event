using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.TagType;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
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
public sealed class NativeTagTypeTagsTests
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
                ICommandHandler<CreateTagTypeTagsCommand, BaseCommandResponse<Guid>>>();
            var update = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<UpdateTagTypeTagsCommand, BaseCommandResponse<Guid>>>();
            var delete = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<DeleteTagTypeTagsCommand, bool>>();

            CreateTagTypeTagsDto[] invalidCreates =
            [
                new() { TagId = Guid.Empty, TagTypeId = AlphaTypeId },
                new() { TagId = Guid.NewGuid(), TagTypeId = AlphaTypeId },
                new() { TagId = data.Gamma.Id, TagTypeId = 0 },
                new() { TagId = data.Gamma.Id, TagTypeId = int.MaxValue },
                new() { TagId = data.Foreign.Id, TagTypeId = ZetaTypeId },
                new() { TagId = data.Alpha.Id, TagTypeId = AlphaTypeId }
            ];
            foreach (var dto in invalidCreates)
            {
                var rejected = await create.ExecuteAsync(new CreateTagTypeTagsCommand
                {
                    TagTypeTagsDto = dto
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(rejected.Errors).IsNotNull();
                await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(3);
            }

            var created = await create.ExecuteAsync(new CreateTagTypeTagsCommand
            {
                TagTypeTagsDto = new()
                {
                    TagId = data.Gamma.Id,
                    TagTypeId = AlphaTypeId,
                    TenantId = data.OtherTenantId
                }
            }, CancellationToken.None);
            await Assert.That(created.IsSuccess).IsTrue();
            await Assert.That(created.Id).IsNotEqualTo(Guid.Empty);
            await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
            await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(4);
            var wireResult = JsonSerializer.SerializeToElement(created, JsonSerializerOptions.Web);
            await Assert.That(wireResult.GetProperty("success").GetBoolean()).IsTrue();

            UpdateTagTypeTagsDto[] invalidUpdates =
            [
                new(),
                new() { Relationship = new() },
                new() { Relationship = new() { TagId = Guid.Empty } },
                new() { Relationship = new() { TagId = Guid.NewGuid() } },
                new() { Relationship = new() { TagTypeId = 0 } },
                new() { Relationship = new() { TagTypeId = int.MaxValue } },
                new() { Relationship = new() { TagId = data.Foreign.Id } },
                new() { Relationship = new() { TagId = data.Alpha.Id } }
            ];
            foreach (var dto in invalidUpdates)
            {
                var rejected = await update.ExecuteAsync(new UpdateTagTypeTagsCommand
                {
                    TagTypeTagsId = created.Id,
                    TagTypeTagsDto = dto
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(rejected.Errors).IsNotNull();
                await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
                await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(4);
            }

            foreach (var inaccessibleId in new[] { Guid.NewGuid(), data.ForeignLinkId })
            {
                var rejected = await update.ExecuteAsync(new UpdateTagTypeTagsCommand
                {
                    TagTypeTagsId = inaccessibleId,
                    TagTypeTagsDto = new()
                    {
                        Relationship = new() { TagId = data.Beta.Id }
                    }
                }, CancellationToken.None);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(await delete.ExecuteAsync(
                    new DeleteTagTypeTagsCommand(inaccessibleId), CancellationToken.None)).IsFalse();
                await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);
                await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(4);
            }

            var unchanged = await update.ExecuteAsync(new UpdateTagTypeTagsCommand
            {
                TagTypeTagsId = created.Id,
                TagTypeTagsDto = new()
                {
                    Relationship = new() { TagTypeId = AlphaTypeId }
                }
            }, CancellationToken.None);
            await Assert.That(unchanged.IsSuccess).IsTrue();
            await Assert.That(unchanged.Id).IsEqualTo(created.Id);
            await AssertRelationshipAsync(db, created.Id, data.Gamma.Id, AlphaTypeId);

            var movedTag = await update.ExecuteAsync(new UpdateTagTypeTagsCommand
            {
                TagTypeTagsId = created.Id,
                TagTypeTagsDto = new()
                {
                    Relationship = new() { TagId = data.Beta.Id, TagTypeId = ZetaTypeId }
                }
            }, CancellationToken.None);
            await Assert.That(movedTag.IsSuccess).IsTrue();
            await Assert.That(movedTag.Id).IsEqualTo(created.Id);
            await AssertRelationshipAsync(db, created.Id, data.Beta.Id, ZetaTypeId);
            await AssertRelationshipAsync(db, data.AlphaLinkId, data.Alpha.Id, AlphaTypeId);

            await Assert.That(await delete.ExecuteAsync(
                new DeleteTagTypeTagsCommand(created.Id), CancellationToken.None)).IsTrue();
            await Assert.That(await db.TagTypeTags.AnyAsync(link => link.Id == created.Id)).IsFalse();
            await Assert.That(await delete.ExecuteAsync(
                new DeleteTagTypeTagsCommand(created.Id), CancellationToken.None)).IsFalse();
            await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(3);
        }

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.OtherTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var foreign = await db.TagTypeTags.AsNoTracking().SingleAsync();
            await Assert.That(foreign.Id).IsEqualTo(data.ForeignLinkId);
            await Assert.That(foreign.TagId).IsEqualTo(data.Foreign.Id);
            await Assert.That(foreign.TagTypeId).IsEqualTo(AlphaTypeId);
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
            IQueryHandler<GetTagTypeTagsDetailsRequest, TagTypeTagsDto?>>();
        var list = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetTagTypeTagsListRequest, List<TagTypeTagsListDto>>>();
        var tags = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetTagsByTagTypeRequest, List<TagListDto>>>();
        var types = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetTagTypesForTagRequest, List<TagTypeListDto>>>();
        var grouped = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetTagsGroupedByTagTypeRequest, List<TagTypeWithTagsDto>>>();

        var found = await detail.QueryAsync(
            new GetTagTypeTagsDetailsRequest(data.AlphaLinkId), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the current tenant's relationship.");
        await Assert.That(found.Id).IsEqualTo(data.AlphaLinkId);
        await Assert.That(found.TagId).IsEqualTo(data.Alpha.Id);
        await Assert.That(found.TagTypeId).IsEqualTo(AlphaTypeId);
        await Assert.That(found.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(await detail.QueryAsync(
            new GetTagTypeTagsDetailsRequest(Guid.NewGuid()), CancellationToken.None)).IsNull();
        await Assert.That(await detail.QueryAsync(
            new GetTagTypeTagsDetailsRequest(data.ForeignLinkId), CancellationToken.None)).IsNull();

        var links = await list.QueryAsync(new GetTagTypeTagsListRequest(), CancellationToken.None);
        await Assert.That(links.Select(link => link.TagId))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id, data.Gamma.Id });
        var byType = await tags.QueryAsync(
            new GetTagsByTagTypeRequest { TagTypeId = AlphaTypeId }, CancellationToken.None);
        await Assert.That(byType.Select(tag => tag.Id))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id });
        await Assert.That(await tags.QueryAsync(
            new GetTagsByTagTypeRequest { TagTypeId = UnusedTypeId }, CancellationToken.None)).IsEmpty();
        var forTag = await types.QueryAsync(
            new GetTagTypesForTagRequest(data.Alpha.Id), CancellationToken.None);
        await Assert.That(forTag).IsEquivalentTo(new[]
        {
            new TagTypeListDto { Id = AlphaTypeId, MasterCode = "ALPHA_TYPE", FullName = "Alpha type" }
        });
        await Assert.That(await types.QueryAsync(
            new GetTagTypesForTagRequest(data.Foreign.Id), CancellationToken.None)).IsEmpty();
        var groups = await grouped.QueryAsync(new GetTagsGroupedByTagTypeRequest(), CancellationToken.None);
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Id).IsEqualTo(AlphaTypeId);
        await Assert.That(groups[1].Id).IsEqualTo(ZetaTypeId);
        await Assert.That(groups.SelectMany(group => group.Tags).Select(tag => tag.Id))
            .IsEquivalentTo(new[] { data.Alpha.Id, data.Beta.Id, data.Gamma.Id });
        await Assert.That(await db.TagTypeTags.CountAsync()).IsEqualTo(3);
        await Assert.That(db.ChangeTracker.Entries()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();

    }

    [Test]
    public async Task AnonymousGroupedHttp_ReturnsOrderedTenantProjectionWithoutUnlinkedTypes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tagtype/with-tags");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var groups = await response.Content.ReadFromJsonAsync<List<TagTypeWithTagsDto>>()
            ?? throw new InvalidOperationException("Expected a grouped tag array.");

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Id).IsEqualTo(AlphaTypeId);
        await Assert.That(groups[0].FullName).IsEqualTo("Alpha type");
        await Assert.That(groups[0].Description).IsEqualTo("Alpha description");
        await Assert.That(groups[0].Tags.Count).IsEqualTo(2);
        await Assert.That(groups[0].Tags[0]).IsEqualTo(new TagListDto
        {
            Id = data.Alpha.Id,
            MasterCode = "ALPHA",
            FullName = "Alpha tag"
        });
        await Assert.That(groups[0].Tags[1]).IsEqualTo(new TagListDto
        {
            Id = data.Beta.Id,
            MasterCode = "BETA",
            FullName = "Beta tag"
        });
        await Assert.That(groups[1].Id).IsEqualTo(ZetaTypeId);
        await Assert.That(groups[1].FullName).IsEqualTo("Zeta type");
        await Assert.That(groups[1].Description).IsNull();
        await Assert.That(groups[1].Tags.Single()).IsEqualTo(new TagListDto
        {
            Id = data.Gamma.Id,
            MasterCode = "GAMMA",
            FullName = "Gamma tag"
        });
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
        foreach (var group in payload.RootElement.EnumerateArray())
        {
            await Assert.That(group.TryGetProperty("tenantId", out _)).IsFalse();
            foreach (var tag in group.GetProperty("tags").EnumerateArray())
            {
                await Assert.That(tag.TryGetProperty("tenantId", out _)).IsFalse();
                await Assert.That(tag.TryGetProperty("tenant", out _)).IsFalse();
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
            db.TagTypeTags.RemoveRange(await db.TagTypeTags.ToListAsync());
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tagtype/with-tags");
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
            .GetProperty("/api/tagtype/with-tags").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString())
            .IsEqualTo("GetTagTypesWithTags");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "TagType" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        var method = typeof(TagTypeController).GetMethod(nameof(TagTypeController.GetWithTags))!;
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
            .IsEquivalentTo(new[] { "id", "fullName", "description", "tags" });
        await Assert.That(properties.GetProperty("id").GetProperty("format").GetString()).IsEqualTo("int32");
        await Assert.That(properties.GetProperty("fullName").GetProperty("type").GetString()).IsEqualTo("string");
        var tags = ResolveSchema(root, properties.GetProperty("tags"));
        await Assert.That(tags.GetProperty("type").GetString()).IsEqualTo("array");
        var tagProperties = ResolveSchema(root, tags.GetProperty("items")).GetProperty("properties");
        await Assert.That(tagProperties.GetProperty("id").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(tagProperties.TryGetProperty("concurrencyStamp", out _)).IsFalse();
        await Assert.That(tagProperties.TryGetProperty("tenantId", out _)).IsFalse();
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
        ExploreDbContext db, Guid id, Guid tagId, int tagTypeId)
    {
        var persisted = await db.TagTypeTags.AsNoTracking().SingleAsync(link => link.Id == id);
        await Assert.That(persisted.TagId).IsEqualTo(tagId);
        await Assert.That(persisted.TagTypeId).IsEqualTo(tagTypeId);
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
            Slug = "native-tag-links",
            FullName = "Native tag links",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "other-tag-links",
            FullName = "Other tag links",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        db.Tenants.AddRange(tenant, otherTenant);
        var alphaType = new TagType
        {
            Id = AlphaTypeId,
            MasterCode = "ALPHA_TYPE",
            FullName = "Alpha type",
            Description = "Alpha description"
        };
        var zetaType = new TagType
        {
            Id = ZetaTypeId, MasterCode = "ZETA_TYPE", FullName = "Zeta type"
        };
        db.TagTypes.AddRange(zetaType, alphaType, new TagType
        {
            Id = UnusedTypeId, MasterCode = "UNUSED_TYPE", FullName = "Unused type"
        });
        var alpha = NewTag(tenant, "ALPHA", "Alpha tag");
        var beta = NewTag(tenant, "BETA", "Beta tag");
        var gamma = NewTag(tenant, "GAMMA", "Gamma tag");
        var foreign = NewTag(otherTenant, "FOREIGN", "Foreign tag");
        db.Tags.AddRange(gamma, beta, alpha, foreign);
        var alphaLink = NewLink(tenant, alpha, alphaType);
        var foreignLink = NewLink(otherTenant, foreign, alphaType);
        db.TagTypeTags.AddRange(
            NewLink(tenant, gamma, zetaType),
            NewLink(tenant, beta, alphaType),
            alphaLink,
            foreignLink);
        await db.SaveChangesAsync();
        return new SeedData(otherTenant.Id, alphaLink.Id, foreignLink.Id, alpha, beta, gamma, foreign);
    }

    private static Tag NewTag(Tenant tenant, string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        MasterCode = code,
        FullName = name,
        TenantId = tenant.Id,
        Tenant = tenant
    };

    private static TagTypeTags NewLink(Tenant tenant, Tag tag, TagType type) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.Id,
        Tenant = tenant,
        TagId = tag.Id,
        Tag = tag,
        TagTypeId = type.Id,
        TagType = type
    };

    private sealed record SeedData(
        Guid OtherTenantId,
        Guid AlphaLinkId,
        Guid ForeignLinkId,
        Tag Alpha,
        Tag Beta,
        Tag Gamma,
        Tag Foreign);
}
