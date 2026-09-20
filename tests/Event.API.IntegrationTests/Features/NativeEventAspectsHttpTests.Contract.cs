using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAspectsHttpTests
{
    [Test]
    public async Task OpenApi_PreservesAllTenOperationsAndAspectSchemas()
    {
        await using var factory = new NativeEventAspectsFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Explore.slnx")))
            root = root.Parent;
        await using var stream = File.OpenRead(Path.Combine(root?.FullName
            ?? throw new InvalidOperationException("Repository root not found."), "schemas", "openapi_islamu-event.json"));
        using var shipped = await JsonDocument.ParseAsync(stream);
        var operationCount = 0;
        foreach (var path in shipped.RootElement.GetProperty("paths").EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/event/", StringComparison.Ordinal) &&
                (path.Name.Contains("/aspects/", StringComparison.Ordinal) || path.Name.Contains("/management-aspects/", StringComparison.Ordinal))))
        {
            await Assert.That(JsonElement.DeepEquals(actual.RootElement.GetProperty("paths").GetProperty(path.Name), path.Value)).IsTrue().Because(path.Name);
            operationCount += path.Value.EnumerateObject().Count(property => property.Name is "get" or "post" or "patch" or "delete");
        }
        await Assert.That(operationCount).IsEqualTo(10);
        foreach (var schema in shipped.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(schema => schema.Name.Contains("IslamicAspect", StringComparison.Ordinal) || schema.Name.Contains("TechAspect", StringComparison.Ordinal)))
            await Assert.That(JsonElement.DeepEquals(actual.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema.Name), schema.Value)).IsTrue().Because(schema.Name);
    }
}
