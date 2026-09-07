using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class UserAuthenticationTokenControllerMetadataTests(ContractApiFixture fixture)
{
    private const string CollectionPath = "/api/userauthenticationtoken";
    private const string OpenApiPath = "/openapi/islamu-event.json";

    [Test]
    public async Task ControllerIsAuthenticatedEndpointClass()
    {
        var controllerType = typeof(UserAuthenticationTokenController);

        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
    }

    [Test]
    public async Task ActionsRequireAuthenticationAndAdvertiseAuthFailures()
    {
        foreach (var action in SensitiveActions())
        {
            await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>())
                .IsNotNull()
                .Because($"{action.Name} exposes per-user session metadata or revocation.");

            AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
            AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        }
    }

    [Test]
    public async Task ReadActionsDoNotUseSharedOutputCache()
    {
        foreach (var action in ReadActions())
        {
            await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>())
                .IsNull()
                .Because($"{action.Name} returns user-scoped session metadata.");

            var responseCache = action.GetCustomAttribute<ResponseCacheAttribute>();

            await Assert.That(responseCache).IsNotNull();
            await Assert.That(responseCache!.NoStore).IsTrue();
            await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        }
    }

    [Test]
    public async Task OpenApiDocument_ExposesOnlySafeSessionReadsAndDelete()
    {
        using var response = await fixture.Client.GetAsync(OpenApiPath);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty(CollectionPath);
        var detail = paths.GetProperty($"{CollectionPath}/{{id}}");
        await Assert.That(collection.TryGetProperty("get", out _)).IsTrue();
        await Assert.That(collection.TryGetProperty("post", out _)).IsFalse();
        await Assert.That(detail.TryGetProperty("get", out _)).IsTrue();
        await Assert.That(detail.TryGetProperty("put", out _)).IsFalse();
        await Assert.That(detail.TryGetProperty("delete", out _)).IsTrue();

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        await Assert.That(schemas.TryGetProperty("CreateUserAuthenticationTokenDto", out _)).IsFalse();
        await Assert.That(schemas.TryGetProperty("UpdateUserAuthenticationTokenDto", out _)).IsFalse();
        await Assert.That(SchemaProperties(schemas, "UserAuthenticationTokenDto"))
            .IsEquivalentTo(["id", "provider", "pdsHost", "expiresAt"]);
        await Assert.That(SchemaProperties(schemas, "UserAuthenticationTokenListDto"))
            .IsEquivalentTo(["id", "provider", "pdsHost", "expiresAt"]);
    }

    [Test]
    [Arguments("POST", CollectionPath)]
    [Arguments("PUT", CollectionPath + "/00000000-0000-0000-0000-000000000001")]
    public async Task RawCredentialMutationRoutes_AreNotMapped(string method, string path)
    {
        using var request = fixture.CreateAuthenticatedRequest(new HttpMethod(method), path);
        request.Content = JsonContent.Create(new { });

        using var response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task DeleteMissingSession_IsIdempotent()
    {
        var path = $"{CollectionPath}/{Guid.NewGuid()}";

        using var first = await fixture.Client.SendAsync(
            fixture.CreateAuthenticatedRequest(HttpMethod.Delete, path));
        using var second = await fixture.Client.SendAsync(
            fixture.CreateAuthenticatedRequest(HttpMethod.Delete, path));

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    private static IReadOnlyList<MethodInfo> SensitiveActions()
    {
        return
        [
            Action(nameof(UserAuthenticationTokenController.GetAll)),
            Action(nameof(UserAuthenticationTokenController.GetById)),
            Action(nameof(UserAuthenticationTokenController.Delete))
        ];
    }

    private static IReadOnlyList<MethodInfo> ReadActions()
    {
        return
        [
            Action(nameof(UserAuthenticationTokenController.GetAll)),
            Action(nameof(UserAuthenticationTokenController.GetById))
        ];
    }

    private static MethodInfo Action(string name)
    {
        var action = typeof(UserAuthenticationTokenController).GetMethod(name);
        ArgumentNullException.ThrowIfNull(action);
        return action;
    }

    private static string[] SchemaProperties(JsonElement schemas, string schemaName)
        => schemas.GetProperty(schemaName).GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToArray();

    private static void AssertProducesProblem(MethodInfo method, int statusCode)
    {
        var hasProblemMetadata = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails));

        if (!hasProblemMetadata)
        {
            throw new InvalidOperationException(
                $"{method.Name} must advertise ProblemDetails for HTTP {statusCode}.");
        }
    }
}
