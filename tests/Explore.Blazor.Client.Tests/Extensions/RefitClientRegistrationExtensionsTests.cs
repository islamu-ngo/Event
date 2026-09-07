using System.Net;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.Http;
using Microsoft.AspNetCore.Components;
using Refit;

namespace Explore.Blazor.Client.Tests.Extensions;

public sealed class RefitClientRegistrationExtensionsTests
{
    [Test]
    public async Task AddBffRefitClient_UsesConfiguredBaseAddress()
    {
        // Arrange
        Uri? capturedUri = null;
        using var provider = BuildProvider(
            services => services.AddBffRefitClient<IRefitBaseAddressProbeApi>(
                    (_, client) => client.BaseAddress = new Uri("https://setup.local/"))
                .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(request =>
                {
                    capturedUri = request.RequestUri;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                })));

        var api = provider.GetRequiredService<IRefitBaseAddressProbeApi>();

        // Act
        using var response = await api.GetAsync();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(capturedUri).IsNotNull();
        await Assert.That(capturedUri!.AbsoluteUri).IsEqualTo("https://setup.local/api/probe");
    }

    [Test]
    public async Task AddBffRefitClient_KeepsExplicitBaseAddress_WhenConfiguredByCaller()
    {
        // Arrange
        Uri? capturedUri = null;
        using var provider = BuildProvider(
            services => services.AddBffRefitClient<IRefitBaseAddressProbeApi>(
                    (_, client) => client.BaseAddress = new Uri("https://override.local/"))
                .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(request =>
                {
                    capturedUri = request.RequestUri;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                })));

        var api = provider.GetRequiredService<IRefitBaseAddressProbeApi>();

        // Act
        using var response = await api.GetAsync();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(capturedUri).IsNotNull();
        await Assert.That(capturedUri!.AbsoluteUri).IsEqualTo("https://override.local/api/probe");
    }


    [Test]
    public async Task AddBffRefitClient_ThrowsHelpfulError_WhenBaseAddressMissing()
    {
        // Arrange
        using var provider = BuildProvider(
            services => services.AddBffRefitClient<IRefitBaseAddressProbeApi>()
                .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(_ =>
                    new HttpResponseMessage(HttpStatusCode.OK))));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IRefitBaseAddressProbeApi>());

        // Assert
        await Assert.That(exception.Message).Contains("A BaseAddress is required for BFF Refit client");
    }

    private static ServiceProvider BuildProvider(
        Func<IServiceCollection, IHttpClientBuilder> registerClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigationManager("https://test.local/"));
        services.AddTransient<BrowserCredentialsMessageHandler>();
        services.AddTransient<BffAntiforgeryMessageHandler>();
        services.AddTransient<BffUnauthorizedHandler>();

        registerClient(services);

        return services.BuildServiceProvider();
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri)
        {
            Initialize(baseUri, baseUri);
        }
    }

    public interface IRefitBaseAddressProbeApi
    {
        [Get("/api/probe")]
        Task<HttpResponseMessage> GetAsync();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
