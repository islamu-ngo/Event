using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Snapshots;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class HateoasSnapshotTests(ContractApiFixture fixture)
{
    private readonly ContractApiFixture _fixture = fixture;

    [Test]
    public async Task EventList_AnonymousContract_MatchesSnapshot()
    {
        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=5");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();

        await VerifyJson(NormalizeJson(json), CreateSnapshotSettings());
    }

    [Test]
    public async Task EventList_AuthenticatedContract_MatchesSnapshot()
    {
        var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/event?pageNumber=1&pageSize=5");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();

        await VerifyJson(NormalizeJson(json), CreateSnapshotSettings());
    }

    [Test]
    public async Task ProblemDetails_NotFoundContract_MatchesSnapshot()
    {
        using var client = CreateClientThatThrows(new NotFoundException("Event", Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var response = await client.GetAsync("/api/actor/11111111-1111-1111-1111-111111111111");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var json = await response.Content.ReadAsStringAsync();

        await VerifyJson(NormalizeJson(json), CreateSnapshotSettings());
    }

    [Test]
    public async Task ProblemDetails_BadRequestContract_MatchesSnapshot()
    {
        using var client = CreateClientThatThrows(new BadRequestException("Invalid input data"));

        var response = await client.GetAsync("/api/actor/22222222-2222-2222-2222-222222222222");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();

        await VerifyJson(NormalizeJson(json), CreateSnapshotSettings());
    }

    private static VerifySettings CreateSnapshotSettings()
    {
        var settings = new VerifySettings();
        settings.UseDirectory("tests/snapshots");
        return settings;
    }

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteElement(document.RootElement, writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (IsVolatileProperty(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteElement(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsVolatileProperty(string propertyName) => propertyName is "traceId" or "timestamp" or "correlationId";

    private HttpClient CreateClientThatThrows(Exception exception)
    {
        var actorRepository = Substitute.For<IActorRepository>();
        actorRepository.GetPublicActorProfileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Actor?>(_ => throw exception);

        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IActorRepository>();
                services.AddSingleton(actorRepository);
            });
        });

        return app.CreateClient();
    }
}
