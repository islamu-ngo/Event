using System.Reflection;
using Explore.Blazor.Client.Pages.Admin.EventTemplates;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class EventTemplateListPageTests
{
    [Test]
    public async Task ServerReloadAsync_AllowsCreate_WhenEmptyCollectionAdvertisesCreateLink()
    {
        var service = Substitute.For<IEventTemplateService>();
        var component = CreateComponent(service);
        service.GetTemplatesAsync(null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventTemplateListDto
            {
                _embedded = new HalCollectionEmbeddedOfEventTemplateListDto { Items = [] },
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 0,
                _links = CreateLinks("create")
            });

        var result = await InvokeServerReloadAsync(component, new GridState<HalResourceOfEventTemplateListDto>
        {
            Page = 0,
            PageSize = 10
        });

        await Assert.That(result.TotalItems).IsEqualTo(0);
        await Assert.That(GetCanCreate(component)).IsTrue();
    }

    [Test]
    public async Task ServerReloadAsync_DoesNotAllowCreate_WhenOnlyFirstItemAdvertisesCreateLink()
    {
        var service = Substitute.For<IEventTemplateService>();
        var component = CreateComponent(service);
        service.GetTemplatesAsync(null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventTemplateListDto
            {
                _embedded = new HalCollectionEmbeddedOfEventTemplateListDto
                {
                    Items =
                    [
                        new HalResourceOfEventTemplateListDto
                        {
                            Id = Guid.NewGuid(),
                            TemplateKey = "conference",
                            DisplayName = "Conference",
                            _links = CreateLinks("create")
                        }
                    ]
                },
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 1
            });

        var result = await InvokeServerReloadAsync(component, new GridState<HalResourceOfEventTemplateListDto>
        {
            Page = 0,
            PageSize = 10
        });

        await Assert.That(result.TotalItems).IsEqualTo(1);
        await Assert.That(GetCanCreate(component)).IsFalse();
    }

    private static EventTemplateListPage CreateComponent(IEventTemplateService service)
    {
        var component = new EventTemplateListPage();
        SetPrivateProperty(component, "TemplateService", service);
        return component;
    }

    private static async Task<GridData<HalResourceOfEventTemplateListDto>> InvokeServerReloadAsync(
        EventTemplateListPage component,
        GridState<HalResourceOfEventTemplateListDto> state)
    {
        var method = typeof(EventTemplateListPage).GetMethod(
            "ServerReloadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ServerReloadAsync not found.");

        var task = (Task<GridData<HalResourceOfEventTemplateListDto>>)method.Invoke(
            component,
            [state, CancellationToken.None])!;

        return await task;
    }

    private static bool GetCanCreate(EventTemplateListPage component)
    {
        var field = typeof(EventTemplateListPage).GetField("_canCreate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_canCreate field not found.");

        return (bool)field.GetValue(component)!;
    }

    private static void SetPrivateProperty<TValue>(object instance, string propertyName, TValue value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{propertyName} property not found.");

        property.SetValue(instance, value);
    }

    private static IDictionary<string, HalLink> CreateLinks(params string[] rels) =>
        rels.ToDictionary(
            rel => rel,
            rel => new HalLink { Href = $"/{rel}", Method = rel == "create" ? "POST" : "GET" },
            StringComparer.Ordinal);
}
