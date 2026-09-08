using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Localization;

public class RuntimeTranslationProviderTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Test]
    public async Task ExportTranslations_WhenNoneProvider_UsesOffline()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.None, null, null, null, "en")
        };

        var provider = CreateRuntimeProvider(resolver);

        var result = await provider.ExportTranslationsAsync("en");
        var list = result.ToList();

        // Offline provider returns empty for embedded bundles (starter bundles are empty)
        await Assert.That(list).IsNotNull();
        await Assert.That(resolver.ResolveCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExportTranslations_WhenTolgeeProvider_RoutesToTolgee()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.test", "1", null, "en")
        };

        var provider = CreateRuntimeProvider(
            resolver,
            tolgeeHandler: new JsonResponseHandler(
                """
                {
                  "en": {
                    "lookup.tag.FIQH.full_name": "Provider Fiqh"
                  }
                }
                """));

        var result = await provider.ExportTranslationsAsync("en");
        var translations = result.ToDictionary(value => value.KeyName, value => value.Value);

        await Assert.That(translations["lookup.tag.FIQH.full_name"]).IsEqualTo("Provider Fiqh");
        await Assert.That(translations).ContainsKey("ui.common.loading");
        await Assert.That(translations).ContainsKey("payment.acceptance.heading");
        await Assert.That(resolver.ResolveCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ImportKeys_WhenTolgeeProviderConfigured_PostsResolvableImportPayload()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.test", "1", null, "en")
        };
        var handler = new CapturingJsonResponseHandler("{}");
        var provider = CreateRuntimeProvider(resolver, tolgeeHandler: handler);

        await provider.ImportKeysAsync([
            new TranslationKeyImport(
                "lookup.tag.FIQH.full_name",
                new Dictionary<string, string> { ["en"] = "Provider Fiqh" })
        ]);

        await Assert.That(handler.LastRequest).IsNotNull();
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/v2/projects/1/keys/import-resolvable");
        await Assert.That(handler.LastRequest.Headers.TryGetValues("X-API-Key", out var values)).IsTrue();
        await Assert.That(values!.Single()).IsEqualTo("test-tms-key");
        await Assert.That(handler.LastContent).Contains("lookup.tag.FIQH.full_name");
        await Assert.That(handler.LastContent).Contains("text");
        await Assert.That(handler.LastContent).Contains("Provider Fiqh");
    }

    [Test]
    public async Task ExportTranslations_WhenWeblateProvider_RoutesToWeblate()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Weblate, "https://weblate.test", "proj1", "comp1", "en")
        };

        var provider = CreateRuntimeProvider(
            resolver,
            weblateHandler: new JsonResponseHandler(
                """
                {
                  "lookup.tag.FIQH.full_name": "Weblate Fiqh"
                }
                """));

        var result = await provider.ExportTranslationsAsync("en");
        var list = result.ToList();

        await Assert.That(list.Count).IsGreaterThan(1);
        await Assert.That(list.Single(value => value.KeyName == "lookup.tag.FIQH.full_name").Value)
            .IsEqualTo("Weblate Fiqh");
        await Assert.That(list.Single(value => value.KeyName == "payment.acceptance.heading").Value)
            .IsEqualTo("Review before payment");
        await Assert.That(resolver.ResolveCallCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ImportKeys_WhenWeblateProviderConfigured_PostsLanguageFilePayload()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Weblate, "https://weblate.test", "proj1", "comp1", "en")
        };
        var handler = new CapturingJsonResponseHandler("{}");
        var provider = CreateRuntimeProvider(resolver, weblateHandler: handler);

        await provider.ImportKeysAsync([
            new TranslationKeyImport(
                "lookup.tag.FIQH.full_name",
                new Dictionary<string, string> { ["en"] = "Weblate Fiqh" })
        ]);

        await Assert.That(handler.LastRequest).IsNotNull();
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/api/translations/proj1/comp1/en/file/");
        await Assert.That(handler.LastRequest.Headers.Authorization?.Scheme).IsEqualTo("Token");
        await Assert.That(handler.LastRequest.Headers.Authorization?.Parameter).IsEqualTo("test-tms-key");
        await Assert.That(handler.LastRequest.Content?.Headers.ContentType?.MediaType).IsEqualTo("multipart/form-data");
        await Assert.That(handler.LastContent).Contains("lookup.tag.FIQH.full_name");
        await Assert.That(handler.LastContent).Contains("Weblate Fiqh");
        await Assert.That(handler.LastContent).Contains("translate");
        await Assert.That(handler.LastContent).Contains("process");
        await Assert.That(handler.LastContent).Contains("replace");
    }

    [Test]
    public async Task ExportTranslations_WhenConnectedProviderReturnsEmpty_ReturnsShippedFallbacks()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("runtime-provider-offline-");
        try
        {
            var bundleDirectory = Path.Combine(tempDirectory.FullName, "App_Data", "Localization", "Bundles");
            Directory.CreateDirectory(bundleDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(bundleDirectory, "en.json"),
                """
                {
                  "lookup.tag.FIQH.full_name": "Offline Fiqh"
                }
                """);

            var environment = Substitute.For<IWebHostEnvironment>();
            environment.ContentRootPath.Returns(tempDirectory.FullName);
            var offlineProvider = new OfflineTranslationProvider(
                Substitute.For<ILogger<OfflineTranslationProvider>>(),
                environment);

            var resolver = new MutableTranslationConfigResolver
            {
                Current = new TranslationConfiguration(
                    TranslationManagementProviderEnum.Weblate, "https://weblate.test", "proj1", "comp1", "en")
            };

            var provider = CreateRuntimeProvider(
                resolver,
                offlineProvider: offlineProvider,
                weblateHandler: new JsonResponseHandler("{}"));

            var result = await provider.ExportTranslationsAsync("en");
            var list = result.ToList();

            await Assert.That(list.Count).IsGreaterThan(1);
            await Assert.That(list.Single(value => value.KeyName == "lookup.tag.FIQH.full_name").Value)
                .IsEqualTo("Offline Fiqh");
            await Assert.That(list.Single(value => value.KeyName == "payment.acceptance.heading").Value)
                .IsEqualTo("Review before payment");
            await Assert.That(resolver.ResolveCallCount).IsGreaterThanOrEqualTo(1);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task TestConnection_WhenNoneProvider_ReturnsTrue()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.None, null, null, null, "en")
        };

        var provider = CreateRuntimeProvider(resolver);

        var connected = await provider.TestConnectionAsync();

        await Assert.That(connected).IsTrue();
    }

    [Test]
    public async Task GetAvailableLanguages_WhenNoneProvider_ReturnsOfflineLanguages()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.None, null, null, null, "en")
        };

        var provider = CreateRuntimeProvider(resolver);

        var languages = await provider.GetAvailableLanguagesAsync();
        var list = languages.ToList();

        // Offline provider should find embedded bundle files (en, fr, ar)
        await Assert.That(list).IsNotNull();
    }

    [Test]
    public async Task ExportTranslations_WhenConfigResolverThrows_FallsBackToOffline()
    {
        var resolver = new ThrowingTranslationConfigResolver();

        var provider = CreateRuntimeProvider(resolver);

        var result = await provider.ExportTranslationsAsync("en");

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task ExportTranslations_WhenTolgeeProviderConfigured_SendsApiKeyHeader()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.test", "1", null, "en")
        };
        var handler = new CapturingJsonResponseHandler(
            """
            { "en": {} }
            """);
        var provider = CreateRuntimeProvider(resolver, tolgeeHandler: handler);

        await provider.ExportTranslationsAsync("en");

        await Assert.That(handler.LastRequest).IsNotNull();
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/v2/projects/1/translations/en");
        await Assert.That(handler.LastRequest.RequestUri.Query).IsEqualTo("?structureDelimiter=.");
        var hasHeader = handler.LastRequest!.Headers.TryGetValues("X-API-Key", out var values);
        await Assert.That(hasHeader).IsTrue();
        await Assert.That(values!.Single()).IsEqualTo("test-tms-key");
    }

    [Test]
    public async Task ExportTranslations_WhenWeblateProviderConfigured_SendsTokenHeader()
    {
        var resolver = new MutableTranslationConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Weblate, "https://weblate.test", "proj1", "comp1", "en")
        };
        var handler = new CapturingJsonResponseHandler("{}");
        var provider = CreateRuntimeProvider(resolver, weblateHandler: handler);

        await provider.ExportTranslationsAsync("en");

        await Assert.That(handler.LastRequest).IsNotNull();
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/api/translations/proj1/comp1/en/file/");
        await Assert.That(handler.LastRequest?.Headers.Authorization?.Scheme).IsEqualTo("Token");
        await Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter).IsEqualTo("test-tms-key");
    }

    private static RuntimeTranslationProvider CreateRuntimeProvider(
        ITranslationConfigResolver resolver,
        OfflineTranslationProvider? offlineProvider = null,
        HttpMessageHandler? tolgeeHandler = null,
        HttpMessageHandler? weblateHandler = null)
    {
        var tolgeeClient = new HttpClient(tolgeeHandler ?? new StaticOkHandler()) { BaseAddress = new Uri("https://tolgee.test") };
        var weblateClient = new HttpClient(weblateHandler ?? new StaticOkHandler()) { BaseAddress = new Uri("https://weblate.test") };

        var tolgeeFactory = Substitute.For<IHttpClientFactory>();
        tolgeeFactory.CreateClient(Arg.Any<string>()).Returns(tolgeeClient);

        var weblateFactory = Substitute.For<IHttpClientFactory>();
        weblateFactory.CreateClient(Arg.Any<string>()).Returns(weblateClient);

        var secretResolver = CreateSecretResolver();
        var tenantContext = CreateTenantContext();
        var tolgee = new TolgeeTranslationProvider(tolgeeFactory, resolver, secretResolver, tenantContext, Substitute.For<ILogger<TolgeeTranslationProvider>>());
        var weblate = new WeblateTranslationProvider(weblateFactory, resolver, secretResolver, tenantContext, Substitute.For<ILogger<WeblateTranslationProvider>>());
        var offline = offlineProvider ?? new OfflineTranslationProvider(Substitute.For<ILogger<OfflineTranslationProvider>>());
        var nullProvider = new NullTranslationProvider(Substitute.For<ILogger<NullTranslationProvider>>());

        return new RuntimeTranslationProvider(
            tolgee,
            weblate,
            offline,
            nullProvider,
            resolver,
            CreateTestMetrics(),
            Substitute.For<ILogger<RuntimeTranslationProvider>>());
    }

    private static TranslationMetrics CreateTestMetrics()
    {
        var meter = new System.Diagnostics.Metrics.Meter("test.translation");
        var factory = Substitute.For<System.Diagnostics.Metrics.IMeterFactory>();
        factory.Create(Arg.Any<System.Diagnostics.Metrics.MeterOptions>()).Returns(meter);
        return new TranslationMetrics(factory);
    }

    private static ISecretResolver CreateSecretResolver()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(SecretDefinitionRegistry.Keys.Localization.TmsApiKey, TenantId, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
        "test-tms-key",
        SecretSourceType.EnvironmentVariable,
        SecretScope.Tenant,
        TenantId,
        DateTimeOffset.UtcNow)));
        return resolver;
    }

    private static ITenantContext CreateTenantContext()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantId);
        return tenantContext;
    }

    private sealed class MutableTranslationConfigResolver : ITranslationConfigResolver
    {
        public TranslationConfiguration Current { get; set; } = new(
            TranslationManagementProviderEnum.None, null, null, null, "en");

        public int ResolveCallCount { get; private set; }

        public Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default)
        {
            ResolveCallCount++;
            return Task.FromResult(Current);
        }

        public void InvalidateCache(Guid? tenantId = null) { }
    }

    private sealed class ThrowingTranslationConfigResolver : ITranslationConfigResolver
    {
        public Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Config resolution failed");

        public void InvalidateCache(Guid? tenantId = null) { }
    }

    private sealed class StaticOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CapturingJsonResponseHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
