using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class ConsumerSecretAcquisitionTests
{
    [Test]
    [Arguments(SecretConsumer.Api, "/keycloak")]
    [Arguments(SecretConsumer.Api, "/cerbos")]
    [Arguments(SecretConsumer.Api, "/provisioning/keycloak")]
    [Arguments(SecretConsumer.Api, "/infrastructure/cerbos")]
    [Arguments(SecretConsumer.Api, "/runtime/bff/oidc/google")]
    [Arguments(SecretConsumer.Bff, "/database")]
    [Arguments(SecretConsumer.Combined, "/provisioning/cerbos")]
    [Arguments(SecretConsumer.Combined, "/runtime/combined/data-protection")]
    public async Task ForbiddenRootStopsBeforeAuthentication(SecretConsumer consumer, string path)
    {
        using var transport = new Authority();
        using var provider = new InfisicalConfigurationProvider(Source(transport, consumer, path));
        await Assert.That(() => provider.Load()).Throws<InvalidOperationException>();
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
        await Assert.That(provider.TryGet("KEYCLOAK_ADMIN_PASSWORD", out _)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("/provisioning/keycloak")]
    [Arguments("/runtime/api/authentication/keycloak/child")]
    public async Task InvalidProvenanceDoesNotPublishPartialConfiguration(string? returnedPath)
    {
        using var transport = new Authority { ReturnedPath = returnedPath, OverridePath = true };
        using var provider = new InfisicalConfigurationProvider(
            Source(transport, SecretConsumer.Api, "/runtime/api/authentication/keycloak"));
        await Assert.That(() => provider.Load()).Throws<InvalidOperationException>();
        await Assert.That(provider.TryGet("KEYCLOAK_ENDPOINT", out _)).IsFalse();
    }

    [Test]
    public async Task ReferenceExpressionIsRejectedWithoutExpansion()
    {
        using var transport = new Authority { Value = "${/provisioning/keycloak.ADMIN}" };
        using var provider = new InfisicalConfigurationProvider(
            Source(transport, SecretConsumer.Api, "/runtime/api/authentication/keycloak"));
        await Assert.That(() => provider.Load()).Throws<InvalidOperationException>();
        await Assert.That(transport.Expanded).IsFalse();
        await Assert.That(provider.TryGet("KEYCLOAK_ENDPOINT", out _)).IsFalse();
    }

    [Test]
    [Arguments(SecretConsumer.Api)]
    [Arguments(SecretConsumer.Bff)]
    [Arguments(SecretConsumer.Combined)]
    public async Task ConsumerAcquiresOnlyItsDeclaredExactRoots(SecretConsumer consumer)
    {
        using var transport = new Authority();
        string[] paths = consumer switch
        {
            SecretConsumer.Api => ["/runtime/api/authentication/keycloak", "/smtp"],
            SecretConsumer.Bff => ["/runtime/bff/oidc/google"],
            _ => ["/runtime/api/authentication/keycloak", "/runtime/bff/oidc/google", "/smtp"]
        };
        using var provider = new InfisicalConfigurationProvider(Source(transport, consumer, paths));
        provider.Load();
        await Assert.That(transport.Paths.SequenceEqual(paths)).IsTrue();
        await Assert.That(transport.Expanded).IsFalse();
        await Assert.That(provider.TryGet(consumer == SecretConsumer.Bff
            ? "Google:ClientSecret" : "Keycloak:Endpoint", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(transport.Value);
        await Assert.That(provider.TryGet("KEYCLOAK_ADMIN_PASSWORD", out _)).IsFalse();
    }

    [Test]
    public async Task UnauthorizedSelectedIdentityCannotPublishEnvironmentCanary()
    {
        using var transport = new Authority { Deny = true };
        using var provider = new InfisicalConfigurationProvider(
            Source(transport, SecretConsumer.Api, "/smtp"));
        await Assert.That(() => provider.Load()).Throws<InvalidOperationException>();
        await Assert.That(provider.TryGet("MAIL_SMTP_PASSWORD", out _)).IsFalse();
        await Assert.That(transport.Paths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DirectSecretReadRejectsForeignRootsBeforeReauthentication()
    {
        using var transport = new Authority();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://authority.test") };
        int authentications = 0;
        var client = new InfisicalClientFactory.InfisicalClientFacade(http, Guid.CreateVersion7().ToString("N"),
            _ => { authentications++; return Task.CompletedTask; }, SecretConsumer.Api, "test");
        await Assert.That(async () => await client.GetSecretAsync("test",
            "/provisioning/keycloak", "ADMIN")).Throws<InfisicalOperationException>();
        await Assert.That(authentications).IsEqualTo(0);
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(null)]
    [Arguments("/smtp/child")]
    [Arguments("/provisioning/cerbos")]
    public async Task DirectReadRequiresExactProviderProvenance(string? returnedPath)
    {
        using var transport = new Authority { OverridePath = true, ReturnedPath = returnedPath };
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://authority.test") };
        var client = new InfisicalClientFactory.InfisicalClientFacade(http, Guid.CreateVersion7().ToString("N"),
            _ => Task.CompletedTask, SecretConsumer.Api, "test");
        await Assert.That(async () => await client.GetSecretAsync("test", "/smtp", "MAIL_SMTP_PASSWORD"))
            .Throws<InfisicalOperationException>();
        await Assert.That(transport.Expanded).IsFalse();
    }

    [Test]
    public async Task DirectReadRetainsProviderRevisionWithoutReferenceExpansion()
    {
        using var transport = new Authority();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://authority.test") };
        var client = new InfisicalClientFactory.InfisicalClientFacade(http, Guid.CreateVersion7().ToString("N"),
            _ => Task.CompletedTask, SecretConsumer.Api, "test");
        var result = await client.GetSecretAsync("test", "/smtp", "MAIL_SMTP_PASSWORD");
        await Assert.That(result!.Value).IsEqualTo(transport.Value);
        await Assert.That(result.Revision).IsEqualTo($"{transport.SecretId}:1");
        await Assert.That(transport.Expanded).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProjectedIdentityAuthenticatesWithoutSharedCredentialFallback(bool wrongIdentity)
    {
        string apiId = Guid.CreateVersion7().ToString("N");
        string apiCredential = SecretsTestValues.CreateSecret();
        string bffId = Guid.CreateVersion7().ToString("N");
        using var transport = new Authority { ExpectedClientId = apiId, ExpectedClientSecret = apiCredential };
        var inputs = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INFISICAL_URL"] = "https://authority.test",
            ["INFISICAL_PROJECT_ID"] = Guid.CreateVersion7().ToString("N"),
            ["INFISICAL_ENV"] = "test",
            ["INFISICAL_API_CLIENT_ID"] = apiId,
            ["INFISICAL_API_CLIENT_SECRET"] = apiCredential,
            ["INFISICAL_BFF_CLIENT_ID"] = bffId,
            ["INFISICAL_BFF_CLIENT_SECRET"] = SecretsTestValues.CreateSecret()
        }).Build();
        var projected = new Dictionary<string, object>();
        RuntimeSecretEnvironment.Apply(projected, inputs, [], SecretConsumer.Api, SecretProviderType.Infisical);
        var source = Source(transport, SecretConsumer.Api, "/smtp");
        source.ClientId = wrongIdentity ? bffId : (string)projected["SecretProvider__Infisical__ClientId"];
        source.ClientSecret = (string)projected["SecretProvider__Infisical__ClientSecret"];
        using var provider = new InfisicalConfigurationProvider(source);
        if (wrongIdentity)
        {
            await Assert.That(() => provider.Load()).Throws<InvalidOperationException>();
            await Assert.That(transport.Paths.Count).IsEqualTo(0);
            await Assert.That(provider.TryGet("MAIL_SMTP_PASSWORD", out _)).IsFalse();
        }
        else
        {
            provider.Load();
            await Assert.That(provider.TryGet("MAIL_SMTP_PASSWORD", out var value)).IsTrue();
            await Assert.That(value).IsEqualTo(transport.Value);
        }
    }

    private static InfisicalConfigurationSource Source(
        Authority transport, SecretConsumer consumer, params string[] paths) => new()
        {
            Url = "https://authority.test",
            ProjectId = Guid.CreateVersion7().ToString("N"),
            ClientId = Guid.CreateVersion7().ToString("N"),
            ClientSecret = SecretsTestValues.CreateSecret(),
            Environment = "test",
            Consumer = consumer,
            Paths = [.. paths],
            MessageHandlerFactory = () => transport
        };

    private sealed class Authority : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> Paths { get; } = [];
        public string Value { get; init; } = SecretsTestValues.CreateSecret();
        public string? ReturnedPath { get; init; }
        public bool OverridePath { get; init; }
        public bool Deny { get; init; }
        public bool Expanded { get; private set; }
        public string SecretId { get; } = Guid.CreateVersion7().ToString("N");
        public string? ExpectedClientId { get; init; }
        public string? ExpectedClientSecret { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.Method == HttpMethod.Post)
            {
                var login = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(
                    cancellationToken: cancellationToken);
                bool denied = Deny || ExpectedClientId is not null
                    && (login!["clientId"] != ExpectedClientId || login["clientSecret"] != ExpectedClientSecret);
                return new HttpResponseMessage(denied ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { accessToken = SecretsTestValues.CreateSecret() })
                };
            }
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            string path = query["secretPath"].ToString();
            Paths.Add(path);
            Expanded |= query["recursive"] != "false" || query["expandSecretReferences"] != "false";
            string key = path switch
            {
                "/smtp" => "MAIL_SMTP_PASSWORD",
                "/runtime/bff/oidc/google" => "GOOGLE_CLIENT_SECRET",
                _ => "KEYCLOAK_ENDPOINT"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    secrets = new[] { new
                    {
                        secretKey = key, secretValue = Value,
                        id = SecretId, version = 1,
                        secretPath = OverridePath ? ReturnedPath : path
                    } }
                })
            };
        }
    }
}
