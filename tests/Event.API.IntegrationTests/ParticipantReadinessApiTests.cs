using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Filters;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests;

[ClassDataSource<ContractApiFixture>(
    Shared = SharedType.PerAssembly)]
public sealed class ParticipantReadinessApiTests(
    ContractApiFixture fixture)
{
    private static readonly Assembly ApiAssembly =
        typeof(AdmissionTicketController).Assembly;
    private static readonly Assembly ApplicationAssembly =
        typeof(HalResource<>).Assembly;

    [Test]
    public async Task ExactReadinessResourceIsPrivateBoundedAndNoStore()
    {
        Type? controller = ApiAssembly.GetType(
            "Explore.API.Controllers." +
            "ParticipantReadinessController");

        await Assert.That(controller).IsNotNull();
        await Assert.That(
                controller.GetCustomAttribute<
                    EndpointClassificationAttribute>())
            .IsNotNull();
        MethodInfo? read = controller.GetMethod(
            "GetReadiness");
        await Assert.That(read).IsNotNull();
        await Assert.That(
                read!.GetCustomAttribute<HttpGetAttribute>())
            .IsNotNull();
        await Assert.That(
                read.GetCustomAttribute<
                    AllowAnonymousAttribute>())
            .IsNotNull();
        await Assert.That(
                read.GetCustomAttribute<
                    PrivateNoStoreAttribute>())
            .IsNotNull();
        int[] statuses = read
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToArray();
        await Assert.That(
                new[]
                {
                    (int)HttpStatusCode.OK,
                    (int)HttpStatusCode.NotFound,
                }.All(statuses.Contains))
            .IsTrue();
    }

    [Test]
    public async Task ReadinessContractContainsOnlyBoundedNonPiiState()
    {
        Type? resource = ApplicationAssembly.GetType(
            "Explore.Application.DTOs.Admissions." +
            "ParticipantReadinessDto");

        await Assert.That(resource).IsNotNull();
        string[] properties = resource!.GetProperties()
            .Where(property =>
                property.GetCustomAttribute<
                    System.Text.Json.Serialization
                        .JsonIgnoreAttribute>() is null)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(properties)
            .IsEquivalentTo([
                "ActiveAdmissionAvailable",
                "RegistrationTicketAssignmentId",
                "StatusCode",
                "SupportCode",
            ]);
        string[] forbidden =
        [
            "email",
            "phone",
            "name",
            "address",
            "answer",
            "consenttext",
        ];
        await Assert.That(properties.Any(property =>
                forbidden.Any(fragment =>
                    property.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
    }

    [Test]
    public async Task SubjectAndOrganizerActionsAreWriteProtected()
    {
        Type? controller = ApiAssembly.GetType(
            "Explore.API.Controllers." +
            "ParticipantReadinessController");
        await Assert.That(controller).IsNotNull();
        string[] methods =
        [
            "Complete",
            "Approve",
            "Revoke",
        ];
        foreach (string methodName in methods)
        {
            MethodInfo? method =
                controller!.GetMethod(methodName);
            await Assert.That(method).IsNotNull();
            await Assert.That(
                    method!.GetCustomAttribute<
                        HttpPostAttribute>())
                .IsNotNull();
            await Assert.That(
                    method.GetCustomAttribute<
                        AuthorizeAttribute>())
                .IsNotNull();
            await Assert.That(
                    method.GetCustomAttribute<
                        EnableRateLimitingAttribute>())
                .IsNotNull();
        }
    }

    [Test]
    public async Task HalRelationsExposeOnlyAuthorizedReadinessActions()
    {
        string[] required =
        [
            "CompleteParticipantReadiness",
            "ApproveParticipantReadiness",
            "RevokeParticipantReadiness",
        ];
        string[] fields = typeof(LinkRelations)
            .GetFields(
                BindingFlags.Public
                | BindingFlags.Static)
            .Select(field => field.Name)
            .ToArray();

        await Assert.That(required.All(fields.Contains))
            .IsTrue();
    }

    [Test]
    public async Task OpenApiPublishesExactResourceAndOptionalCapabilityHeader()
    {
        using HttpResponseMessage response =
            await fixture.Client.GetAsync(
                "/openapi/islamu-event.json");
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        const string path =
            "/api/events/{eventId}/participant-readiness/" +
            "registration-orders/{orderId}/participants/" +
            "{participantId}/assignments/{assignmentId}";

        await Assert.That(
                root.GetProperty("paths")
                    .TryGetProperty(path, out _))
            .IsTrue();
        JsonElement operation = root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("get");
        bool hasCapability = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Any(parameter =>
                parameter.GetProperty("name").GetString() ==
                "X-Registration-Order-Capability"
                && parameter.GetProperty("in").GetString() ==
                "header"
                && (!parameter.TryGetProperty(
                        "required",
                        out JsonElement required)
                    || !required.GetBoolean()));
        await Assert.That(hasCapability).IsTrue();

        JsonElement schema = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ParticipantReadinessDto");
        string raw = schema.GetRawText();
        await Assert.That(raw.Contains(
                "email",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
        await Assert.That(raw.Contains(
                "answer",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
    }

    [Test]
    public async Task InvalidCapabilityIsGenericPrivateProblemDetails()
    {
        string capability = Guid.CreateVersion7()
            .ToString("N");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ExactResourcePath());
        request.Headers.Add(
            "X-Registration-Order-Capability",
            capability);

        using HttpResponseMessage response =
            await fixture.Client.SendAsync(request);
        string body =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType)
            .Satisfies(
                contentType =>
                    contentType?.MediaType ==
                    "application/problem+json",
                "returns ProblemDetails");
        await Assert.That(body).DoesNotContain(capability);
        await AssertPrivateNoStore(response);
    }

    [Test]
    public async Task UnauthenticatedReadinessWriteFailsClosed()
    {
        using HttpResponseMessage response =
            await fixture.Client.PostAsync(
                $"{ExactResourcePath()}/complete",
                content: null);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateNoStore(response);
    }

    private static string ExactResourcePath() =>
        $"/api/events/{Guid.CreateVersion7()}/" +
        "participant-readiness/registration-orders/" +
        $"{Guid.CreateVersion7()}/participants/" +
        $"{Guid.CreateVersion7()}/assignments/" +
        $"{Guid.CreateVersion7()}";

    private static async Task AssertPrivateNoStore(
        HttpResponseMessage response)
    {
        CacheControlHeaderValue? cacheControl =
            response.Headers.CacheControl;
        await Assert.That(cacheControl).IsNotNull();
        await Assert.That(cacheControl!.Private).IsTrue();
        await Assert.That(cacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.TryGetValues(
                "Referrer-Policy",
                out IEnumerable<string>? values))
            .IsTrue();
        await Assert.That(values)
            .Contains("no-referrer");
    }
}
