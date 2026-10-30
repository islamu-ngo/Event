using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Extensions;

namespace Explore.Blazor.Client.Tests;

/// <summary>
/// Exercises the generated clients as consumers, across the HTTP and JSON boundary.
/// </summary>
public class ApiClientNamingTests
{
    private static readonly Guid ResourceId = Guid.Parse("0198eb23-4a10-7000-8000-000000000001");
    private static readonly Guid ParentId = Guid.Parse("0198eb23-4a10-7000-8000-000000000002");

    [Test]
    public async Task GeneratedTagClientsCoverCurrentSchemaAndCanBeConstructed()
    {
        using var schema = ReadSchema();
        var tagCount = schema.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.TryGetProperty("operationId", out _))
            .Select(operation => operation.Value.GetProperty("tags")[0].GetString())
            .Distinct(StringComparer.Ordinal).Count();
        var clients = GeneratedEventApiClients.ClientTypes;
        await Assert.That(clients).IsNotEmpty();
        await Assert.That(clients.Count).IsEqualTo(tagCount);

        using var transport = new ContractTransport("{}");
        using var http = CreateHttpClient(transport);
        foreach (var (contract, implementation) in clients)
        {
            var client = Activator.CreateInstance(implementation, http);
            await Assert.That(contract.IsInstanceOfType(client)).IsTrue();
            await Assert.That(contract.GetMethods().Any(method => method.ReturnType.IsAssignableTo(typeof(Task))))
                .IsTrue().Because($"{contract.Name} must expose a callable asynchronous operation.");
        }
    }

    [Test]
    public async Task GeneratedOperationsHaveSemanticNamesWithoutCollisionSuffixes()
    {
        var names = GeneratedEventApiClients.ClientTypes.SelectMany(pair => pair.InterfaceType.GetMethods())
            .Select(method => method.Name).Distinct(StringComparer.Ordinal).ToArray();
        await Assert.That(names).IsNotEmpty();
        await Assert.That(names.Where(name => !Regex.IsMatch(name, @"^[A-Z][A-Za-z0-9]+Async$", RegexOptions.CultureInvariant)
            || Regex.IsMatch(name, @"\d+Async$", RegexOptions.CultureInvariant)
            || Regex.IsMatch(name, @"^(GET|POST|PUT|DELETE|PATCH)Async$", RegexOptions.CultureInvariant)))
            .IsEmpty();
    }

    [Test]
    public async Task SchemaReferencedHalItemsRemainTypedInCompiledContracts()
    {
        using var schema = ReadSchema();
        var schemas = schema.RootElement.GetProperty("components").GetProperty("schemas");
        var embeddedSchemas = schemas.EnumerateObject()
            .Where(component => component.Value.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("_embedded", out var embedded)
                && embedded.TryGetProperty("$ref", out _))
            .Select(component => component.Value.GetProperty("properties").GetProperty("_embedded")
                .GetProperty("$ref").GetString()!.Split('/')[^1])
            .Distinct(StringComparer.Ordinal)
            .Select(name => (Name: name, Schema: schemas.GetProperty(name)))
            .Where(component => component.Schema.GetProperty("properties").GetProperty("items")
                .GetProperty("items").TryGetProperty("$ref", out _)).ToArray();
        await Assert.That(embeddedSchemas).IsNotEmpty();
        foreach (var component in embeddedSchemas)
        {
            var itemReference = component.Schema.GetProperty("properties").GetProperty("items")
                .GetProperty("items").GetProperty("$ref").GetString()!;
            var expectedItem = typeof(EventDayClient).Assembly.GetType(
                $"{typeof(EventDayClient).Namespace}.{itemReference.Split('/')[^1]}", throwOnError: true)!;
            var embedded = typeof(EventDayClient).Assembly.GetType(
                $"{typeof(EventDayClient).Namespace}.{component.Name}", throwOnError: true)!;
            var items = embedded.GetProperties().Single(property =>
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == "items");
            await Assert.That(items.PropertyType.IsAssignableTo(typeof(IEnumerable<>).MakeGenericType(expectedItem)))
                .IsTrue().Because($"{component.Name} must preserve the schema's item contract {expectedItem.Name}.");
        }
    }

    [Test]
    public async Task AgendaReadsPreserveScheduleValuesAndHalActions()
    {
        var itemJson = $$"""
            {"id":"{{ResourceId:D}}","eventId":"{{ParentId:D}}","title":"Opening discussion","startTime":"2026-10-23T09:30:00+02:00","localStartDate":"2026-10-23","localStartTime":"09:30:00","_links":{"edit":{"href":"/api/eventagendaitem/{{ResourceId:D}}","method":"PATCH"} } }
            """;
        using var transport = new ContractTransport(itemJson, CollectionJson(itemJson));
        using var http = CreateHttpClient(transport);
        IEventAgendaItemClient client = new EventAgendaItemClient(http);
        var detail = await client.GetEventAgendaItemByIdAsync(ResourceId, api_version: "1.0", x_Api_Version: "1.0");
        var collection = await client.GetEventAgendaItemsByEventAsync(ParentId);
        var item = collection._embedded!.Items!.Single();

        await Assert.That(detail.Id).IsEqualTo(ResourceId);
        await Assert.That(detail.StartTime).IsEqualTo(new DateTimeOffset(2026, 10, 23, 9, 30, 0, TimeSpan.FromHours(2)));
        await Assert.That(detail.LocalStartDate!.Value.Day).IsEqualTo(23);
        await Assert.That(detail.LocalStartTime).IsEqualTo(new TimeSpan(9, 30, 0));
        await Assert.That(detail._links!["edit"].Method).IsEqualTo("PATCH");
        await Assert.That(item.Title).IsEqualTo("Opening discussion");
        await Assert.That(item._links!["edit"].Href).IsEqualTo($"/api/eventagendaitem/{ResourceId:D}");
        await Assert.That(collection.TotalCount).IsEqualTo(1);
        await AssertGet(transport.Requests[0], $"/api/eventagendaitem/{ResourceId:D}?api-version=1.0");
        await Assert.That(transport.Requests[0].ApiVersion).IsEqualTo("1.0");
        await AssertGet(transport.Requests[1], $"/api/eventagendaitem/by-event/{ParentId:D}");

        var value = JsonSerializer.Deserialize<EventAgendaItemDto>(itemJson)!;
        var listValue = JsonSerializer.Deserialize<EventAgendaItemListDto>(itemJson)!;
        await Assert.That((value with { Title = "Updated discussion" }).Title).IsEqualTo("Updated discussion");
        await Assert.That(value.Title).IsEqualTo("Opening discussion");
        await Assert.That(listValue.EventId).IsEqualTo(ParentId);
    }

    [Test]
    public async Task DayReadsPreserveLocalDateAndTypedHalItems()
    {
        var itemJson = $$"""
            {"id":"{{ResourceId:D}}","eventId":"{{ParentId:D}}","localDate":"2026-10-23","label":"Friday","allowsDayScopeRegistration":true,"_links":{"self":{"href":"/api/eventday/{{ResourceId:D}}","method":"GET"} } }
            """;
        using var transport = new ContractTransport(itemJson, CollectionJson(itemJson));
        using var http = CreateHttpClient(transport);
        IEventDayClient client = new EventDayClient(http);
        var detail = await client.GetEventDayByIdAsync(ResourceId);
        var collection = await client.GetEventDaysByEventAsync(ParentId);
        var item = collection._embedded!.Items!.Single();

        await Assert.That(detail.Id).IsEqualTo(ResourceId);
        await Assert.That(detail.LocalDate!.Value.Year).IsEqualTo(2026);
        await Assert.That(detail.LocalDate.Value.Month).IsEqualTo(10);
        await Assert.That(detail.LocalDate.Value.Day).IsEqualTo(23);
        await Assert.That(detail.AllowsDayScopeRegistration).IsTrue();
        await Assert.That(item.Label).IsEqualTo("Friday");
        await Assert.That(item._links!["self"].Href).IsEqualTo($"/api/eventday/{ResourceId:D}");
        await Assert.That(collection._links!["self"].Method).IsEqualTo("GET");
        await AssertGet(transport.Requests[0], $"/api/eventday/{ResourceId:D}");
        await AssertGet(transport.Requests[1], $"/api/eventday/by-event/{ParentId:D}");

        var value = JsonSerializer.Deserialize<EventDayDto>(itemJson)!;
        var listValue = JsonSerializer.Deserialize<EventDayListDto>(itemJson)!;
        await Assert.That((value with { Label = "Opening day" }).Label).IsEqualTo("Opening day");
        await Assert.That(value.Label).IsEqualTo("Friday");
        await Assert.That(listValue.LocalDate).IsEqualTo(value.LocalDate);
    }

    [Test]
    public async Task RoomReadsPreserveCapacityAndAbsenceOfEditAuthority()
    {
        var itemJson = $$"""
            {"id":"{{ResourceId:D}}","locationId":"{{ParentId:D}}","name":"Workshop room","capacity":48,"_links":{"self":{"href":"/api/locationroom/{{ResourceId:D}}","method":"GET"} } }
            """;
        using var transport = new ContractTransport(itemJson, CollectionJson(itemJson));
        using var http = CreateHttpClient(transport);
        ILocationRoomClient client = new LocationRoomClient(http);
        var detail = await client.GetLocationRoomByIdAsync(ResourceId);
        var collection = await client.GetLocationRoomsByLocationAsync(ParentId);
        var item = collection._embedded!.Items!.Single();

        await Assert.That(detail.LocationId).IsEqualTo(ParentId);
        await Assert.That(detail.Capacity).IsEqualTo(48);
        await Assert.That(detail._links!.ContainsKey("edit")).IsFalse();
        await Assert.That(item.Name).IsEqualTo("Workshop room");
        await Assert.That(item.Capacity).IsEqualTo(48);
        await Assert.That(item._links!.ContainsKey("edit")).IsFalse();
        await AssertGet(transport.Requests[0], $"/api/locationroom/{ResourceId:D}");
        await AssertGet(transport.Requests[1], $"/api/locationroom/by-location/{ParentId:D}");

        var value = JsonSerializer.Deserialize<LocationRoomDto>(itemJson)!;
        var listValue = JsonSerializer.Deserialize<LocationRoomListDto>(itemJson)!;
        await Assert.That((value with { Capacity = 60 }).Capacity).IsEqualTo(60);
        await Assert.That(value.Capacity).IsEqualTo(48);
        await Assert.That(listValue.Name).IsEqualTo("Workshop room");
    }

    [Test]
    public async Task TenantStoragePatchSendsOptionalValueAndConsumesCommandResult()
    {
        using var transport = new ContractTransport($$"""{"id":"{{ResourceId:D}}","success":true}""");
        using var http = CreateHttpClient(transport);
        ITenantStorageSettingsClient client = new TenantStorageSettingsClient(http);
        var result = await client.PatchTenantStorageSettingsAsync(new PatchTenantStorageSettingsDto
        {
            Policy = new PatchTenantStoragePolicyDto
            {
                Provider = new OptionalUpdateOfstring { HasValue = true, Value = "Local" }
            }
        });

        var request = transport.Requests.Single();
        await AssertPatch(request, "/api/tenant/settings/storage");
        using var body = JsonDocument.Parse(request.Body!);
        var provider = body.RootElement.GetProperty("policy").GetProperty("provider");
        await Assert.That(provider.GetProperty("hasValue").GetBoolean()).IsTrue();
        await Assert.That(provider.GetProperty("value").GetString()).IsEqualTo("Local");
        await Assert.That(result.Id).IsEqualTo(ResourceId);
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task TenantBrandingPatchPreservesConcurrencyAndReturnsHalDocument()
    {
        var response = $$"""
            {"documentKey":"branding","schemaVersion":1,"defaultsVersion":"1","payload":{},"source":"Tenant","sourceScopeId":"{{ParentId:D}}","concurrencyStamp":"{{ResourceId:D}}","_links":{"edit":{"href":"/api/tenant/settings/documents/branding","method":"PATCH"} } }
            """;
        using var transport = new ContractTransport(response);
        using var http = CreateHttpClient(transport);
        ITenantSettingsDocumentsClient client = new TenantSettingsDocumentsClient(http);
        var result = await client.PatchTenantBrandingSettingsDocumentAsync(new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = ResourceId,
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Community" }
            }
        });

        var request = transport.Requests.Single();
        await AssertPatch(request, "/api/tenant/settings/documents/branding");
        using var body = JsonDocument.Parse(request.Body!);
        await Assert.That(body.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(ResourceId);
        await Assert.That(body.RootElement.GetProperty("displayName").GetProperty("value").GetProperty("value").GetString())
            .IsEqualTo("Community");
        await Assert.That(result.ConcurrencyStamp).IsEqualTo(ResourceId);
        await Assert.That(result.DocumentKey).IsEqualTo("branding");
        await Assert.That(result._links!["edit"].Method).IsEqualTo("PATCH");
    }

    [Test]
    public async Task TenantFooterPatchPreservesClearAndReplacementPresence()
    {
        using var transport = new ContractTransport("""{"success":true}""");
        using var http = CreateHttpClient(transport);
        IFooterClient client = new FooterClient(http);
        await client.PatchTenantFooterSettingsAsync(new PatchTenantFooterSettingsDto
        {
            General = new PatchTenantFooterGeneralDto
            {
                Enabled = new OptionalUpdateOfboolean { HasValue = true, Value = false }
            },
            Description = new PatchTenantFooterDescriptionDto
            {
                Text = new OptionalUpdateOfstring { HasValue = true, Value = null }
            }
        });

        var request = transport.Requests.Single();
        await AssertPatch(request, "/api/footer/settings");
        using var body = JsonDocument.Parse(request.Body!);
        var enabled = body.RootElement.GetProperty("general").GetProperty("enabled");
        await Assert.That(enabled.GetProperty("hasValue").GetBoolean()).IsTrue();
        await Assert.That(enabled.GetProperty("value").GetBoolean()).IsFalse();
        var clear = body.RootElement.GetProperty("description").GetProperty("text");
        await Assert.That(clear.GetProperty("hasValue").GetBoolean()).IsTrue();
        await Assert.That(!clear.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null).IsTrue();
        await Assert.That(!body.RootElement.TryGetProperty("template", out var template) || template.ValueKind == JsonValueKind.Null)
            .IsTrue();
    }

    [Test]
    public async Task TenantFooterReadPreservesServerSuppliedAffordances()
    {
        using var transport = new ContractTransport("""{"_links":{"edit":{"href":"/api/footer/settings","method":"PATCH"}}}""");
        using var http = CreateHttpClient(transport);
        IFooterClient client = new FooterClient(http);
        var resource = await client.GetTenantFooterSettingsAsync();
        await AssertGet(transport.Requests.Single(), "/api/footer/settings");
        await Assert.That(resource._links!["edit"].Href).IsEqualTo("/api/footer/settings");
        await Assert.That(resource._links["edit"].Method).IsEqualTo("PATCH");
        await Assert.That(resource._links.ContainsKey("delete")).IsFalse();
    }

    [Test]
    public async Task SchedulingNotFoundPreservesProblemDetails()
    {
        using var transport = new ContractTransport("""{"type":"https://event.test/problems/not-found","title":"Agenda item not found","status":404,"detail":"No visible item.","traceId":"synthetic-trace"}""")
        {
            StatusCode = HttpStatusCode.NotFound
        };
        using var http = CreateHttpClient(transport);
        IEventAgendaItemClient client = new EventAgendaItemClient(http);
        var error = (await Assert.ThrowsAsync<ApiException<ProblemDetails>>(() => client.GetEventAgendaItemByIdAsync(ResourceId)))!;
        await Assert.That(error.StatusCode).IsEqualTo(404);
        await Assert.That(error.Result.Title).IsEqualTo("Agenda item not found");
        await Assert.That(error.Result.Detail).IsEqualTo("No visible item.");
        await Assert.That(((JsonElement)error.Result.AdditionalProperties["traceId"]).GetString()).IsEqualTo("synthetic-trace");
        await AssertGet(transport.Requests.Single(), $"/api/eventagendaitem/{ResourceId:D}");
    }

    [Test]
    public async Task TenantSettingsDenialIsNotConsumedAsSuccessfulCommand()
    {
        using var transport = new ContractTransport("""{"title":"Forbidden","status":403,"detail":"Settings are locked."}""")
        {
            StatusCode = HttpStatusCode.Forbidden
        };
        using var http = CreateHttpClient(transport);
        ITenantStorageSettingsClient client = new TenantStorageSettingsClient(http);
        var error = (await Assert.ThrowsAsync<ApiException<ProblemDetails>>(() =>
            client.PatchTenantStorageSettingsAsync(new PatchTenantStorageSettingsDto())))!;
        await Assert.That(error.StatusCode).IsEqualTo(403);
        await Assert.That(error.Result.Detail).IsEqualTo("Settings are locked.");
        await AssertPatch(transport.Requests.Single(), "/api/tenant/settings/storage");
    }

    private static JsonDocument ReadSchema() => JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../schemas/openapi_islamu-event.json"))));

    private static HttpClient CreateHttpClient(HttpMessageHandler transport) =>
        new(transport, disposeHandler: false) { BaseAddress = new Uri("https://event.test/") };

    private static string CollectionJson(string item) =>
        $$"""{"pageNumber":1,"pageSize":20,"totalCount":1,"_links":{"self":{"href":"/api/schedule","method":"GET"} },"_embedded":{"items":[{{item}}]} }""";

    private static async Task AssertGet(CapturedRequest request, string pathAndQuery)
    {
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.Uri.AbsoluteUri).IsEqualTo($"https://event.test{pathAndQuery}");
        await Assert.That(request.Body).IsNull();
    }

    private static async Task AssertPatch(CapturedRequest request, string path)
    {
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Patch);
        await Assert.That(request.Uri.AbsoluteUri).IsEqualTo($"https://event.test{path}");
        await Assert.That(request.ContentType).IsEqualTo("application/json");
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? ApiVersion, string? ContentType, string? Body);

    private sealed class ContractTransport(params string[] responses) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!,
                request.Headers.TryGetValues("X-Api-Version", out var versions) ? versions.Single() : null,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(responses[Requests.Count - 1], Encoding.UTF8,
                    StatusCode == HttpStatusCode.OK ? "application/hal+json" : "application/problem+json")
            };
        }
    }
}
