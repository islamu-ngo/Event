namespace Explore.Blazor.Client.Tests.Helpers;

public sealed class EventTemplateHalResourceExtensionsTests
{
    [Test]
    public async Task GeneratedCollection_PreservesCollectionAndItemLinks()
    {
        var templateId = Guid.NewGuid();
        var collection = new HalCollectionResourceOfEventTemplateListDto
        {
            PageNumber = 2,
            PageSize = 10,
            TotalCount = 11,
            _links = new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = "/api/EventTemplate", Method = "POST", Title = "Create event template" }
            },
            _embedded = new HalCollectionEmbeddedOfEventTemplateListDto
            {
                Items =
                [
                    HalLinkTestFactory.WithLinks(new HalResourceOfEventTemplateListDto
                    {
                        Id = templateId,
                        TenantId = Guid.NewGuid(),
                        TemplateKey = "conference",
                        DisplayName = "Conference",
                        EventTypeId = 1,
                        Version = 3,
                        IsPublished = true,
                        IsActive = true,
                        DefinitionCount = 4
                    },
            new HalLinkTestLink("edit", $"/api/EventTemplate/{templateId}", "PUT", "Edit"),
            new HalLinkTestLink("delete", $"/api/EventTemplate/{templateId}", "DELETE", "Delete"))
                ]
            }
        };

        var item = collection._embedded!.Items!.Single();

        await Assert.That(collection.PageNumber).IsEqualTo(2);
        await Assert.That(collection.TotalCount).IsEqualTo(11);
        await Assert.That(collection._links!.ContainsKey("create")).IsTrue();
        await Assert.That(collection._links["create"].Method).IsEqualTo("POST");
        await Assert.That(collection._embedded.Items.Count).IsEqualTo(1);
        await Assert.That(item.Id).IsEqualTo(templateId);
        await Assert.That(item.DefinitionCount).IsEqualTo(4);
        await Assert.That(item._links!.ContainsKey("edit")).IsTrue();
        await Assert.That(item._links.ContainsKey("delete")).IsTrue();
        await Assert.That(item._links["edit"].Method).IsEqualTo("PUT");
    }

    [Test]
    public async Task GeneratedCollection_PreservesLinks_WhenCollectionIsEmpty()
    {
        var collection = new HalCollectionResourceOfEventTemplateListDto
        {
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0,
            _links = new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = "/api/EventTemplate", Method = "POST" }
            },
            _embedded = new HalCollectionEmbeddedOfEventTemplateListDto { Items = [] }
        };

        await Assert.That(collection._embedded!.Items!).IsEmpty();
        await Assert.That(collection._links!.ContainsKey("create")).IsTrue();
    }
}
