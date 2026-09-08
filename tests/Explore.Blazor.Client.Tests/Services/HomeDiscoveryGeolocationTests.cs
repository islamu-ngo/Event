using System.Text.Json;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Services.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class HomeDiscoveryGeolocationTests
{
    [Test]
    public async Task BrowserResultDeserializesStringStatus()
    {
        const string json = """
            {"status":"available","latitude":50.8466,"longitude":4.3528}
            """;

        var result = JsonSerializer.Deserialize<HomeDiscoveryGeolocationResult>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(HomeDiscoveryGeolocationStatus.Available);
        await Assert.That(result.Latitude).IsEqualTo(50.8466);
        await Assert.That(result.Longitude).IsEqualTo(4.3528);
    }

    [Test]
    public async Task GetCurrentPositionAsyncUsesTypedModuleBoundary()
    {
        var expected = new HomeDiscoveryGeolocationResult(
            HomeDiscoveryGeolocationStatus.Available,
            50.85,
            4.35);
        var module = Substitute.For<IJSObjectReference>();
        module.InvokeAsync<HomeDiscoveryGeolocationResult>(
                "getCurrentPosition",
                Arg.Any<CancellationToken>(),
                Arg.Any<object?[]?>())
            .Returns(expected);
        var jsRuntime = Substitute.For<IJSRuntime>();
        jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                Arg.Any<CancellationToken>(),
                Arg.Is<object?[]?>(arguments =>
                    arguments != null &&
                    arguments.Length == 1 &&
                    Equals(arguments[0], "/js/home-discovery.js")))
            .Returns(module);
        await using var interop = new HomeDiscoveryGeolocation(
            jsRuntime,
            Substitute.For<ILogger<HomeDiscoveryGeolocation>>());

        var result = await interop.GetCurrentPositionAsync();

        await Assert.That(result).IsEqualTo(expected);
        await module.Received(1).InvokeAsync<HomeDiscoveryGeolocationResult>(
            "getCurrentPosition",
            Arg.Any<CancellationToken>(),
            Arg.Any<object?[]?>());
    }

    [Test]
    public async Task GetCurrentPositionAsyncFailsClosedWhenInteropIsUnavailable()
    {
        await using var interop = new HomeDiscoveryGeolocation(
            new ThrowingJsRuntime(),
            Substitute.For<ILogger<HomeDiscoveryGeolocation>>());

        var result = await interop.GetCurrentPositionAsync();

        await Assert.That(result.Status).IsEqualTo(HomeDiscoveryGeolocationStatus.Unavailable);
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
    }

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("prerender");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            throw new InvalidOperationException("prerender");
    }
}
