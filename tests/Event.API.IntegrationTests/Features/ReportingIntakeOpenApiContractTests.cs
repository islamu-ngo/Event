using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class ReportingIntakeOpenApiContractTests(ContractApiFixture fixture)
{
    private const string OpenApiEndpoint = "/openapi/islamu-event.json";
    private const string PolicyPath = "/api/tenant/settings/reporting-intake";

    [Test]
    public async Task NativeDocument_ExposesStableAuthenticatedReportingIntakeOperations()
    {
        using WebApplicationFactory<Program> factory =
            fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Keycloak:AuthorizationUrl"] =
                                "https://identity.example.test/realms/event/protocol/openid-connect/auth"
                        });
                });
            });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response =
            await client.GetAsync(OpenApiEndpoint);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement path = document.RootElement.GetProperty("paths").GetProperty(PolicyPath);
        JsonElement get = path.GetProperty("get");
        JsonElement put = path.GetProperty("put");

        await Assert.That(get.GetProperty("operationId").GetString())
            .IsEqualTo("GetTenantReportingIntakePolicy");
        await Assert.That(put.GetProperty("operationId").GetString())
            .IsEqualTo("UpdateTenantReportingIntakePolicy");
        await Assert.That(get.GetProperty("x-endpoint-class").GetString())
            .IsEqualTo("Authenticated");
        await Assert.That(put.GetProperty("x-endpoint-class").GetString())
            .IsEqualTo("Authenticated");
        await Assert.That(RequiresKeycloak(document.RootElement, get))
            .IsTrue();
        await Assert.That(RequiresKeycloak(document.RootElement, put))
            .IsTrue();
    }

    [Test]
    public async Task NativeDocument_RegistersReportingIntakeHalAndStringEnumSchemas()
    {
        using var response = await fixture.Client.GetAsync(OpenApiEndpoint);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        await Assert.That(schemas.TryGetProperty("TenantReportingIntakePolicyDto", out _)).IsTrue();
        await Assert.That(schemas.TryGetProperty("UpdateTenantReportingIntakePolicyDto", out _)).IsTrue();
        await Assert.That(schemas.TryGetProperty("HalResourceOfTenantReportingIntakePolicyDto", out _)).IsTrue();

        JsonElement source = schemas.GetProperty("SettingSource");
        await Assert.That(source.GetProperty("type").GetString()).IsEqualTo("string");
        string[] values = source.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        await Assert.That(values).Contains("SystemDefault");
        await Assert.That(values).Contains("SystemLocked");
        await Assert.That(values).Contains("TenantOverride");
    }

    private static bool RequiresKeycloak(
        JsonElement document,
        JsonElement operation)
    {
        JsonElement security = operation.TryGetProperty(
            "security",
            out JsonElement operationSecurity)
            ? operationSecurity
            : document.GetProperty("security");
        return security.ValueKind == JsonValueKind.Array
            && security.EnumerateArray().Any(requirement =>
                requirement.ValueKind == JsonValueKind.Object
                && requirement.TryGetProperty("Keycloak", out _));
    }
}
