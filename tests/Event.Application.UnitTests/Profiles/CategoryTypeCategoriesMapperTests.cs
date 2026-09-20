using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class CategoryTypeCategoriesMapperTests
{
    private static readonly Guid LinkId = Guid.Parse("01900000-0000-7000-8000-000000000120");
    private static readonly Guid CategoryId = Guid.Parse("01900000-0000-7000-8000-000000000121");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000122");

    [Test]
    [Arguments(false, true, true)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, false, false)]
    [Arguments(true, true, true)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, false, false)]
    public async Task Projection_PreservesRelationshipIdsAndNullableLabels(bool list, bool categoryLoaded, bool typeLoaded)
    {
        var source = Source();
        if (!categoryLoaded)
            source.Category = null!;
        if (!typeLoaded)
            source.CategoryType = null!;
        var expected = Expected(list);
        if (!categoryLoaded)
        {
            expected["categoryFullName"] = null;
            expected["categoryMasterCode"] = null;
        }
        if (!typeLoaded)
        {
            expected["categoryTypeFullName"] = null;
            expected["categoryTypeMasterCode"] = null;
        }
        await AssertContract(source, list, expected);
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task Projection_PreservesNullAndEmptyLabels(bool list, string? value)
    {
        var source = Source();
        source.Category.FullName = value!;
        source.Category.MasterCode = value!;
        source.CategoryType.FullName = value!;
        source.CategoryType.MasterCode = value!;
        var expected = Expected(list);
        foreach (var field in new[] { "categoryFullName", "categoryMasterCode", "categoryTypeFullName", "categoryTypeMasterCode" })
            expected[field] = value;
        await AssertContract(source, list, expected);
    }

    [Test]
    public async Task MissingRelationship_RemainsNull() => await Assert.That(CategoryTypeCategoriesMapper.ToDetail(null)).IsNull();

    [Test]
    public async Task Create_RejectsBodyTenantAuthorityAndDoesNotImportNavigationOrIdentity()
    {
        var source = new CreateCategoryTypeCategoriesDto
        {
            CategoryId = CategoryId,
            CategoryTypeId = 17,
            TenantId = Guid.Parse("01900000-0000-7000-8000-000000000123")
        };
        var result = CategoryTypeCategoriesMapper.Create(source, TenantId);
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.CategoryId).IsEqualTo(CategoryId);
        await Assert.That(result.CategoryTypeId).IsEqualTo(17);
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
        await Assert.That(result.Category).IsNull();
        await Assert.That(result.CategoryType).IsNull();
        await Assert.That(result.Tenant).IsNull();
    }

    private static async Task AssertContract(CategoryTypeCategories source, bool list, JsonNode expected)
    {
        var actual = list
            ? JsonSerializer.SerializeToNode(CategoryTypeCategoriesMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(CategoryTypeCategoriesMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    private static JsonNode Expected(bool list)
    {
        var expected = JsonNode.Parse("""
            {"id":"01900000-0000-7000-8000-000000000120","categoryId":"01900000-0000-7000-8000-000000000121",
             "categoryFullName":"Lecture","categoryMasterCode":"LECTURE","categoryTypeId":17,
             "categoryTypeFullName":"Topic","categoryTypeMasterCode":"TOPIC"}
            """)!;
        if (!list)
            expected["tenantId"] = "01900000-0000-7000-8000-000000000122";
        return expected;
    }

    private static CategoryTypeCategories Source()
    {
        var category = new Category { Id = CategoryId, MasterCode = "LECTURE", FullName = "Lecture", TenantId = TenantId, Tenant = null! };
        category.Parent = category;
        return new CategoryTypeCategories
        {
            Id = LinkId,
            CategoryId = CategoryId,
            Category = category,
            CategoryTypeId = 17,
            CategoryType = new CategoryType { Id = 17, MasterCode = "TOPIC", FullName = "Topic", Description = "Not surfaced" },
            TenantId = TenantId,
            Tenant = null!
        };
    }
}
