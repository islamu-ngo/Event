using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Admissions;
using Explore.Blazor.Client.Services.Http;
using ISLAMU.Wire.Contracts.Admissions;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AdmissionCheckInServiceTransportTests
{
    [Test]
    [Arguments(HttpStatusCode.ServiceUnavailable, AdmissionCheckInUiStatus.OnlineRequired)]
    [Arguments(HttpStatusCode.TooManyRequests, AdmissionCheckInUiStatus.Saturated)]
    public async Task ExactProblemStatusUsesTypedUiStatusThroughProductionExecutor(
        HttpStatusCode responseStatus,
        AdmissionCheckInUiStatus expectedStatus)
    {
        using var response = new HttpResponseMessage(responseStatus)
        {
            Content = new StringContent(
                $$"""{"type":"about:blank","title":"Unavailable","status":{{(int)responseStatus}},"detail":"Try later"}""",
                Encoding.UTF8,
                "application/problem+json")
        };
        response.Headers.TryAddWithoutValidation("Retry-After", "120");
        var handler = new ExactResponseHandler(response);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://bff.example") };
        var service = new AdmissionCheckInService(client, new ApiClientExecutor());

        AdmissionCheckInUiResult result = await service.CheckInAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Credential(1),
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(expectedStatus);
        await Assert.That(result.Code).IsEqualTo(AdmissionCheckInUiCodes.Rejected);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
        await Assert.That(typeof(AdmissionCheckInUiResult).GetProperties()
            .Any(property => property.Name.Contains("Retry", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task ActiveCapabilityUsesOnlyDedicatedScannerHeaderTransport()
    {
        const string capability = "scanner-capability-that-stays-in-memory";
        var navigation = new TestNavigationManager();
        using var state = new AdmissionScannerCapabilityState(navigation);
        state.Activate(capability);
        var staffHandler = new CapturingHandler();
        var scannerHandler = new CapturingHandler();
        var capabilityHandler = new AdmissionScannerCapabilityMessageHandler(state)
        {
            InnerHandler = scannerHandler
        };
        using var staffClient = new HttpClient(staffHandler) { BaseAddress = navigation.BaseUriAsUri };
        using var scannerClient = new HttpClient(capabilityHandler) { BaseAddress = navigation.BaseUriAsUri };
        var scannerTransport = new AdmissionScannerHttpClient(new FixedHttpClientFactory(scannerClient));
        var service = new AdmissionCheckInService(
            staffClient,
            scannerTransport,
            state,
            new ApiClientExecutor());
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        AdmissionCredentialBearer credential = Credential(7);

        AdmissionCheckInUiResult result = await service.CheckInAsync(
            eventId,
            targetId,
            credential,
            CancellationToken.None);

        await Assert.That(result.Code).IsEqualTo(AdmissionCheckInUiCodes.CheckedIn);
        await Assert.That(staffHandler.RequestCount).IsEqualTo(0);
        await Assert.That(scannerHandler.RequestCount).IsEqualTo(1);
        await Assert.That(scannerHandler.Path).IsEqualTo("/api/admission/scanner/check-ins");
        await Assert.That(scannerHandler.ScannerCapability).IsEqualTo(capability);
        await Assert.That(scannerHandler.Authorization).IsNull();
        await Assert.That(scannerHandler.Cookie).IsNull();
        await AssertScannerRequestShapeAsync(scannerHandler.Body, credential.Value);
        await Assert.That(scannerHandler.Body).DoesNotContain(eventId.ToString("D"));
        await Assert.That(scannerHandler.Body).DoesNotContain(targetId.ToString("D"));
        await Assert.That(scannerHandler.Body).DoesNotContain(capability);
        await Assert.That(state.ToString()).DoesNotContain(capability);
    }

    [Test]
    public async Task CapabilityClearsOnNavigationAndDisposalAndStaffTransportResumes()
    {
        var navigation = new TestNavigationManager();
        var state = new AdmissionScannerCapabilityState(navigation);
        var staffHandler = new CapturingHandler();
        var scannerHandler = new CapturingHandler();
        var capabilityHandler = new AdmissionScannerCapabilityMessageHandler(state)
        {
            InnerHandler = scannerHandler
        };
        using var staffClient = new HttpClient(staffHandler) { BaseAddress = navigation.BaseUriAsUri };
        using var scannerClient = new HttpClient(capabilityHandler) { BaseAddress = navigation.BaseUriAsUri };
        var service = new AdmissionCheckInService(
            staffClient,
            new AdmissionScannerHttpClient(new FixedHttpClientFactory(scannerClient)),
            state,
            new ApiClientExecutor());
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        AdmissionCredentialBearer credential = Credential(8);
        state.Activate("first-transient-capability");
        long activeGeneration = state.Generation;

        navigation.NavigateTo("studio/events/" + Guid.CreateVersion7().ToString("D"));

        await Assert.That(state.IsActive).IsFalse();
        await Assert.That(state.Generation).IsGreaterThan(activeGeneration);
        await service.CheckInAsync(eventId, targetId, credential, CancellationToken.None);
        await Assert.That(staffHandler.RequestCount).IsEqualTo(1);
        await Assert.That(staffHandler.Path).IsEqualTo($"/api/events/{eventId:D}/admission/check-ins");
        await Assert.That(staffHandler.ScannerCapability).IsNull();
        await AssertStaffRequestShapeAsync(staffHandler.Body, targetId, credential.Value);

        state.Activate("second-transient-capability");
        state.Dispose();

        await Assert.That(state.IsActive).IsFalse();
        await Assert.That(state.ToString()).DoesNotContain("second-transient-capability");
    }

    [Test]
    public async Task DedicatedHandlerRejectsEveryNonScannerDestinationBeforeForwarding()
    {
        var navigation = new TestNavigationManager();
        using var state = new AdmissionScannerCapabilityState(navigation);
        state.Activate("route-bound-capability");
        var destination = new CapturingHandler();
        using var client = new HttpClient(new AdmissionScannerCapabilityMessageHandler(state)
        {
            InnerHandler = destination
        })
        {
            BaseAddress = navigation.BaseUriAsUri
        };

        await Assert.That(async () => await client.PostAsync(
                "/api/events/" + Guid.CreateVersion7().ToString("D") + "/admission/check-ins",
                new StringContent("{}")))
            .Throws<InvalidOperationException>();
        await Assert.That(destination.RequestCount).IsEqualTo(0);
    }

    private static async Task AssertStaffRequestShapeAsync(
        string body,
        Guid expectedTargetId,
        string expectedCredential)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        await Assert.That(root.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["targetId", "credential"]);
        await Assert.That(root.GetProperty("targetId").GetGuid()).IsEqualTo(expectedTargetId);
        await Assert.That(root.GetProperty("credential").GetString()).IsEqualTo(expectedCredential);
    }

    private static async Task AssertScannerRequestShapeAsync(
        string body,
        string expectedCredential)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        await Assert.That(root.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["credential"]);
        await Assert.That(root.GetProperty("credential").GetString()).IsEqualTo(expectedCredential);
    }

    private static AdmissionCredentialBearer Credential(int seed)
    {
        var bytes = new byte[AdmissionCredentialBearer.ByteLength];
        BitConverter.TryWriteBytes(bytes, seed);
        return AdmissionCredentialBearer.FromBytes(bytes);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://bff.example/", "https://bff.example/");

        public Uri BaseUriAsUri => new(BaseUri);

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? Path { get; private set; }
        public string? ScannerCapability { get; private set; }
        public string? Authorization { get; private set; }
        public string? Cookie { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Path = request.RequestUri?.AbsolutePath;
            ScannerCapability = request.Headers.TryGetValues(
                AdmissionScannerCapabilityMessageHandler.HeaderName,
                out IEnumerable<string>? scannerValues)
                    ? scannerValues.SingleOrDefault()
                    : null;
            Authorization = request.Headers.Authorization?.ToString();
            Cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookieValues)
                ? cookieValues.SingleOrDefault()
                : null;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"outcome\":\"CheckedIn\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ExactResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response);
        }
    }
}
