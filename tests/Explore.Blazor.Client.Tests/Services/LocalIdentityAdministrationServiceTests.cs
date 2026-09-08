
using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.ControlPlane;
using Explore.Blazor.Client.Serialization;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class LocalIdentityAdministrationServiceTests
{
    [Test]
    [Arguments(0, 20)]
    [Arguments(1, 0)]
    [Arguments(1, 101)]
    [Arguments(int.MaxValue, 100)]
    public async Task InvalidPagesCannotReachIdentityTransport(int pageNumber, int pageSize)
    {
        using var transport = new LocalIdentityUiTransport();
        using HttpClient http = transport.CreateHttpClient();
        LocalIdentityAdministrationService service = transport.CreateService(http);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetIdentitiesAsync(
            pageNumber: pageNumber, pageSize: pageSize, cancellationToken: CancellationToken));

        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResetCannotSubmitAnotherOperationsPredecessor()
    {
        using var transport = new LocalIdentityUiTransport();
        using HttpClient http = transport.CreateHttpClient();
        LocalIdentityAdministrationService service = transport.CreateService(http);
        var identity = new HalResourceOfLocalIdentitySummary
        {
            LocalSubjectId = transport.SubjectA, CurrentOperationId = transport.OperationA,
            CurrentOperationConcurrencyStamp = transport.StampA, CredentialState = LocalCredentialState.Ready,
            _links = new Dictionary<string, HalLink> { ["issue-temporary-credential"] = new() { Href = transport.ResetPath(transport.SubjectA) } }
        };
        var request = new ResetLocalCredentialRequestDto
        {
            OperationId = Guid.CreateVersion7(), ExpectedCurrentOperationId = transport.OperationB,
            ExpectedCurrentOperationConcurrencyStamp = transport.StampB, Reason = "Exact predecessor"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetAsync(identity, request, CancellationToken));

        await Assert.That(transport.Requests.Any(captured => captured.Method == HttpMethod.Post)).IsFalse();
    }

    [Test]
    public async Task NativeAotContextRoundTripsNullableHalAuthorityAndExtensionData()
    {
        const string payload = """{"pageNumber":1,"pageSize":20,"totalCount":1,"_embedded":{"items":[{"localSubjectId":"01900000-0000-7000-8000-000000000001","credentialState":null,"currentOperationId":null,"currentOperationConcurrencyStamp":null,"_links":{}}]},"_links":{"self":{"href":"/api/instance/local-identities"}},"futurePolicy":{"revision":7}}""";
        var resource = (HalCollectionResourceOfLocalIdentitySummary)JsonSerializer.Deserialize(payload,
            typeof(HalCollectionResourceOfLocalIdentitySummary), AppJsonSerializerContext.Default)!;

        await Assert.That(resource._embedded!.Items!.Single().CredentialState).IsNull();
        string serialized = JsonSerializer.Serialize(resource, typeof(HalCollectionResourceOfLocalIdentitySummary), AppJsonSerializerContext.Default);
        using JsonDocument restored = JsonDocument.Parse(serialized);
        await Assert.That(restored.RootElement.GetProperty("futurePolicy").GetProperty("revision").GetInt32()).IsEqualTo(7);
        await Assert.That(restored.RootElement.GetProperty("_links").GetProperty("self").GetProperty("href").GetString())
            .IsEqualTo(LocalIdentityUiTransport.IdentitiesPath);
    }

    [Test]
    public async Task MissingDiscoveryRefusesListingAfterReadingActualOverview()
    {
        using var handler = new DiscoveryBoundaryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bff.example.test/") };
        var overview = new ControlPlaneApiAdapter(new ControlPlaneClient(http), new ControlPlaneDeploymentModeClient(http),
            new ControlPlaneTenantConfigurationClient(http), new ControlPlaneTenantLifecycleClient(http), new ControlPlaneTenantPlanClient(http));
        var service = new LocalIdentityAdministrationService(new LocalIdentityAdministrationClient(http), overview);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetIdentitiesAsync(cancellationToken: CancellationToken));

        await Assert.That(handler.RequestedPaths.Contains("/api/admin/control-plane/overview")).IsTrue();
        await Assert.That(handler.RequestedPaths.Contains("/api/instance/local-identities")).IsFalse();
    }

    [Test]
    public async Task GeneratedListPreservesNullCredentialStateForInvalidNativeMetadata()
    {
        Guid subjectId = Guid.CreateVersion7();
        string json = JsonSerializer.Serialize(new
        {
            pageNumber = 1, pageSize = 20, totalCount = 1,
            _links = new Dictionary<string, object>(),
            _embedded = new
            {
                items = new[]
                {
                    new
                    {
                        localSubjectId = subjectId, email = "invalid-metadata@example.test",
                        firstName = "Invalid", lastName = "Metadata", emailVerified = true,
                        credentialState = (string?)null, currentOperationId = (Guid?)null,
                        currentOperationConcurrencyStamp = (Guid?)null, _links = new Dictionary<string, object>()
                    }
                }
            }
        });
        using var handler = new JsonResponseHandler(json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bff.example.test/") };
        var client = new LocalIdentityAdministrationClient(http);

        HalCollectionResourceOfLocalIdentitySummary result = await client.ListLocalIdentitiesAsync(
            pageNumber: 1, pageSize: 20, cancellationToken: CancellationToken);

        HalResourceOfLocalIdentitySummary item = result._embedded!.Items!.Single();
        await Assert.That(item.LocalSubjectId).IsEqualTo(subjectId);
        await Assert.That(item.CredentialState).IsNull();
        await Assert.That(item.CurrentOperationId).IsNull();
        await Assert.That(item.CurrentOperationConcurrencyStamp).IsNull();
        await Assert.That(item._links!.ContainsKey("issue-temporary-credential")).IsFalse();
    }

    [Test]
    public async Task GeneratedOperationPreservesNullCredentialStateForNonCurrentNativeOperation()
    {
        Guid operationId = Guid.CreateVersion7();
        Guid subjectId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string json = JsonSerializer.Serialize(new
        {
            receipt = new
            {
                operationId, kind = "Create", stage = "Superseded", initiatingApplicationUserId = actorId,
                localSubjectId = subjectId, applicationUserId = subjectId, personalActorId = Guid.CreateVersion7(),
                externalLoginId = Guid.CreateVersion7(), createdAt = now
            },
            operationConcurrencyStamp = Guid.CreateVersion7(), verifiedByApplicationUserId = actorId,
            verifiedAt = now, updatedAt = now, isCurrent = false, credentialState = (string?)null,
            resetAudit = (object?)null, _links = new Dictionary<string, object>()
        });
        using var handler = new JsonResponseHandler(json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bff.example.test/") };
        var client = new LocalIdentityAdministrationClient(http);

        HalResourceOfLocalCredentialOperationStatus result = await client.GetLocalCredentialOperationAsync(
            operationId: operationId, cancellationToken: CancellationToken);

        await Assert.That(result.Receipt.OperationId).IsEqualTo(operationId);
        await Assert.That(result.IsCurrent).IsFalse();
        await Assert.That(result.CredentialState).IsNull();
        await Assert.That(result._links!.ContainsKey("reconcile")).IsFalse();
    }

    private static CancellationToken CancellationToken => TUnit.Core.TestContext.Current!.Execution.CancellationToken;

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/hal+json")
            });
    }

    private sealed class DiscoveryBoundaryHandler : HttpMessageHandler
    {
        internal List<string> RequestedPaths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"_links":{},"_embedded":{"items":[]}}""", Encoding.UTF8, "application/hal+json")
            });
        }
    }
}

// The boundary records real generated requests; test components and services share no internal substitutes.
internal sealed class LocalIdentityUiTransport : HttpMessageHandler
{
    internal const string IdentitiesPath = "/api/instance/local-identities";
    internal const string OperationsPath = "/api/instance/local-identity-operations";
    internal Guid SubjectA { get; } = Guid.CreateVersion7();
    internal Guid SubjectB { get; } = Guid.CreateVersion7();
    internal Guid OperationA { get; set; } = Guid.CreateVersion7();
    internal Guid OperationB { get; } = Guid.CreateVersion7();
    internal Guid StampA { get; set; } = Guid.CreateVersion7();
    internal LocalCredentialState StateA { get; set; } = LocalCredentialState.Ready;
    internal Guid StampB { get; } = Guid.CreateVersion7();
    internal bool Discoverable { get; set; } = true;
    internal bool ActionLinks { get; set; } = true;
    internal int PageCount { get; set; } = 1;
    internal bool IncludeSubjectB { get; set; } = true;
    internal List<Request> Requests { get; } = [];
    internal Func<Request, CancellationToken, Task<HttpResponseMessage>>? Override { get; set; }
    internal Func<Request, CancellationToken, Task<HttpResponseMessage>>? ListOverride { get; set; }
    internal HttpClient CreateHttpClient() => new(this, disposeHandler: false) { BaseAddress = new Uri("https://bff.example.test/") };
    internal ControlPlaneApiAdapter CreateOverview(HttpClient http) => new(new ControlPlaneClient(http),
        new ControlPlaneDeploymentModeClient(http), new ControlPlaneTenantConfigurationClient(http),
        new ControlPlaneTenantLifecycleClient(http), new ControlPlaneTenantPlanClient(http));
    internal LocalIdentityAdministrationService CreateService(HttpClient http) => new(new LocalIdentityAdministrationClient(http), CreateOverview(http));
    internal string ResetPath(Guid subjectId) => $"{IdentitiesPath}/{subjectId:D}/temporary-credential";
    internal static string StatusPath(Guid operationId) => $"{OperationsPath}/{operationId:D}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        JsonElement? body = null;
        if (request.Content is not null)
        {
            string text = await request.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                using JsonDocument document = JsonDocument.Parse(text);
                body = document.RootElement.Clone();
            }
        }
        var captured = new Request(request.Method, request.RequestUri!, body);
        Requests.Add(captured);
        if (ListOverride is not null && captured.Method == HttpMethod.Get && captured.Uri.AbsolutePath == IdentitiesPath)
            return await ListOverride(captured, cancellationToken);
        if (Override is not null && captured.Uri.AbsolutePath != "/api/admin/control-plane/overview"
            && !(captured.Method == HttpMethod.Get && captured.Uri.AbsolutePath == IdentitiesPath))
            return await Override(captured, cancellationToken);
        return DefaultResponse(captured);
    }

    internal HttpResponseMessage DefaultResponse(Request request)
    {
        if (request.Uri.AbsolutePath == "/api/admin/control-plane/overview")
            return Json(new { _links = Discoverable ? Links("local-identities", IdentitiesPath) : new Dictionary<string, object>() });
        if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == IdentitiesPath)
        {
            int page = int.Parse(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.Uri.Query)["pageNumber"].ToString(),
                System.Globalization.CultureInfo.InvariantCulture);
            return Json(new
            {
                pageNumber = page, pageSize = 20, totalCount = (PageCount - 1) * 20 + (IncludeSubjectB ? 2 : 1),
                totalPages = PageCount, hasNext = page < PageCount, hasPrevious = page > 1,
                _links = ActionLinks ? Links("create-local-identity", IdentitiesPath) : new Dictionary<string, object>(),
                _embedded = new { items = IncludeSubjectB
                    ? new[] { Row(SubjectA, OperationA, StampA, "Account A"), Row(SubjectB, OperationB, StampB, "Account B") }
                    : new[] { Row(SubjectA, OperationA, StampA, "Account A") } }
            });
        }
        throw new InvalidOperationException("Unexpected Local administration transport operation.");
    }

    private object Row(Guid subjectId, Guid operationId, Guid stamp, string firstName) => new
    {
        localSubjectId = subjectId, email = $"{subjectId:N}@example.test", firstName, lastName = "Owner",
        emailVerified = true, credentialState = (subjectId == SubjectA ? StateA : LocalCredentialState.Ready).ToString(), currentOperationId = operationId,
        currentOperationConcurrencyStamp = stamp,
        _links = ActionLinks ? Links("issue-temporary-credential", ResetPath(subjectId)) : new Dictionary<string, object>()
    };
    internal object Status(Guid operationId, Guid subjectId, LocalCredentialState state, bool reconcile) => new
    {
        receipt = new
        {
            operationId, kind = "Create", stage = state == LocalCredentialState.ProvisioningPending ? "ProvisioningPending" : "ChangeRequired",
            initiatingApplicationUserId = SubjectA, localSubjectId = subjectId, applicationUserId = subjectId,
            personalActorId = Guid.CreateVersion7(), externalLoginId = Guid.CreateVersion7(), createdAt = DateTimeOffset.UtcNow
        },
        operationConcurrencyStamp = subjectId == SubjectA ? StampA : StampB, verifiedByApplicationUserId = SubjectA,
        verifiedAt = DateTimeOffset.UtcNow, updatedAt = (DateTimeOffset?)null, isCurrent = true,
        credentialState = state.ToString(), resetAudit = (object?)null,
        _links = reconcile ? Links("reconcile", StatusPath(operationId) + "/reconcile") : new Dictionary<string, object>()
    };
    internal HttpResponseMessage Issue(Guid operationId, Guid subjectId, string password, HttpStatusCode statusCode = HttpStatusCode.OK) => Json(new
    {
        outcome = "Issued", operation = Status(operationId, subjectId, LocalCredentialState.ChangeRequired, reconcile: false), temporaryPassword = password,
        _links = Links("self", StatusPath(operationId))
    }, statusCode);
    internal static HttpResponseMessage Json(object value, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/hal+json")
    };
    private static Dictionary<string, object> Links(string relation, string href) => new() { [relation] = new { href } };
    internal sealed record Request(HttpMethod Method, Uri Uri, JsonElement? Body)
    {
        public override string ToString() => nameof(Request);
    }
}
