using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Domain.Keycloak;
using Explore.Domain.Secrets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed class KeycloakOperationHttpTests
{
    [Test]
    public async Task Connection_IsPrivateNoStoreAndPublishesServerAuthoredLinks()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);

        using HttpResponseMessage response = await client.GetAsync(
            "/api/instance/keycloak/connection");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK)
            .Because(body);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).Contains("\"_links\"");
        await Assert.That(body).Contains("\"inspect\"");
        await Assert.That(body).Contains("\"plan\"");
    }

    [Test]
    public async Task Inspect_NeverReturnsFreshAdministratorCredentials()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);
        string username = $"admin-{Guid.CreateVersion7():N}";
        string password = $"password-{Guid.CreateVersion7():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/instance/keycloak/inspect",
            new
            {
                administratorUsername = username,
                administratorPassword = password
            });
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK)
            .Because(body);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).DoesNotContain(username);
        await Assert.That(body).DoesNotContain(password);
        await Assert.That(body).Contains("\"plan\"");
    }

    [Test]
    public async Task Apply_UsesPersistedReceiptAndPublishesReconcileAffordance()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);
        object credentials = new
        {
            administratorUsername = $"admin-{Guid.CreateVersion7():N}",
            administratorPassword = $"password-{Guid.CreateVersion7():N}"
        };
        using HttpResponseMessage planResponse = await client.PostAsJsonAsync(
            "/api/instance/keycloak/plans",
            credentials);
        using JsonDocument plan = JsonDocument.Parse(
            await planResponse.Content.ReadAsStringAsync());
        Guid operationId = plan.RootElement.GetProperty("id").GetGuid();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/instance/keycloak/operations/{operationId:D}/apply",
            credentials);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK)
            .Because(body);
        await Assert.That(factory.Repository.Operation!.Id)
            .IsEqualTo(operationId);
        await Assert.That(factory.Repository.Operation.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(body).Contains("\"reconcile\"");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Receipt_WhenActorChanges_IsForbiddenWithoutReceiptDisclosure()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);
        object credentials = Credentials();
        using HttpResponseMessage planResponse = await client.PostAsJsonAsync(
            "/api/instance/keycloak/plans",
            credentials);
        using JsonDocument plan = JsonDocument.Parse(
            await planResponse.Content.ReadAsStringAsync());
        Guid operationId = plan.RootElement.GetProperty("id").GetGuid();
        string digest = plan.RootElement.GetProperty("digest").GetString()!;
        factory.Authority.Actor =
            Guid.Parse("88888888-8888-7888-8888-888888888888")
                .ToString("D");

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/instance/keycloak/operations/{operationId:D}");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Forbidden)
            .Because(body);
        await Assert.That(body).DoesNotContain(digest);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(factory.AdminClient.ApplyCount).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_WhenSetupGenerationChanges_IsForbiddenBeforeProviderContact()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);
        object credentials = Credentials();
        using HttpResponseMessage planResponse = await client.PostAsJsonAsync(
            "/api/instance/keycloak/plans",
            credentials);
        using JsonDocument plan = JsonDocument.Parse(
            await planResponse.Content.ReadAsStringAsync());
        Guid operationId = plan.RootElement.GetProperty("id").GetGuid();
        factory.Authority.SetupGeneration++;

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/instance/keycloak/operations/{operationId:D}/apply",
            credentials);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Forbidden)
            .Because(body);
        await Assert.That(factory.Repository.Operation!.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
        await Assert.That(factory.AdminClient.ApplyCount).IsEqualTo(0);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Apply_WhenReceiptExpires_ReturnsConflictWithoutProviderContact()
    {
        await using var factory = new KeycloakOperationFactory();
        using HttpClient client = factory.CreateClient();
        Authenticate(client);
        object credentials = Credentials();
        using HttpResponseMessage planResponse = await client.PostAsJsonAsync(
            "/api/instance/keycloak/plans",
            credentials);
        using JsonDocument plan = JsonDocument.Parse(
            await planResponse.Content.ReadAsStringAsync());
        Guid operationId = plan.RootElement.GetProperty("id").GetGuid();
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(16);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/instance/keycloak/operations/{operationId:D}/apply",
            credentials);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Conflict)
            .Because(body);
        await Assert.That(factory.Repository.Operation!.State)
            .IsEqualTo(KeycloakOperationState.Expired);
        await Assert.That(factory.AdminClient.ApplyCount).IsEqualTo(0);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();

        using HttpResponseMessage receiptResponse = await client.GetAsync(
            $"/api/instance/keycloak/operations/{operationId:D}");
        string receiptBody =
            await receiptResponse.Content.ReadAsStringAsync();
        await Assert.That(receiptResponse.StatusCode)
            .IsEqualTo(HttpStatusCode.OK)
            .Because(receiptBody);
        await Assert.That(receiptBody).DoesNotContain("\"apply\"");
        await Assert.That(receiptBody).DoesNotContain("\"cancel\"");
    }

    private static object Credentials() =>
        new
        {
            administratorUsername = $"admin-{Guid.CreateVersion7():N}",
            administratorPassword = $"password-{Guid.CreateVersion7():N}"
        };

    private sealed class KeycloakOperationFactory
        : AuthenticatedWebApplicationFactory
    {
        public InMemoryOperationRepository Repository { get; } = new();
        public FixedAuthority Authority { get; } = new();
        public UnknownOutcomeAdminClient AdminClient { get; } = new();
        public FixedTimeProvider Clock { get; } = new();

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                IAdminContext admin = Substitute.For<IAdminContext>();
                admin.UserId.Returns(
                    Guid.Parse("99999999-9999-7999-8999-999999999999"));
                admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>())
                    .Returns(true);
                services.RemoveAll<IAdminContext>();
                services.AddSingleton(admin);

                services.RemoveAll<IKeycloakOperatorAuthority>();
                services.AddSingleton<IKeycloakOperatorAuthority>(
                    Authority);
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton<ISecretResolver>(
                    new FixedSecretResolver());
                services.RemoveAll<IKeycloakAdminClient>();
                services.AddSingleton<IKeycloakAdminClient>(
                    new FixedInspectionClient());
                services.RemoveAll<IKeycloakOperationRepository>();
                services.AddSingleton<IKeycloakOperationRepository>(
                    Repository);
                services.RemoveAll<IKeycloakOperationCoordinator>();
                services.AddSingleton<IKeycloakOperationCoordinator>(
                    new InMemoryCoordinator(Repository));
                services.RemoveAll<IKeycloakAdminOperationClient>();
                services.AddSingleton<IKeycloakAdminOperationClient>(
                    AdminClient);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(
                    Clock);
            });
        }
    }

    private static void Authenticate(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(
                Guid.Parse("99999999-9999-7999-8999-999999999999")));
    }

    private sealed class FixedAuthority : IKeycloakOperatorAuthority
    {
        public string Actor { get; set; } =
            Guid.Parse("99999999-9999-7999-8999-999999999999")
                .ToString("D");

        public long SetupGeneration { get; set; } = 7;

        public Task<KeycloakOperatorAuthority> RequireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new KeycloakOperatorAuthority(
                Guid.Parse("77777777-7777-7777-8777-777777777777"),
                Actor,
                SetupGeneration,
                IsSetupAuthority: false));
    }

    private sealed class FixedSecretResolver : ISecretResolver
    {
        private static readonly IReadOnlyDictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SecretDefinitionRegistry.Keys.Keycloak.Endpoint] =
                    "https://identity.example.test",
                [SecretDefinitionRegistry.Keys.Keycloak.Realm] =
                    "operators",
                [SecretDefinitionRegistry.Keys.Keycloak.ClientId] =
                    "event-bff",
                [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] =
                    $"runtime-{Guid.CreateVersion7():N}"
            };

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(settingKey, out string? value)
                ? SecretResolutionResult.Resolved(new ResolvedSecret(
                    settingKey,
                    value,
                    SecretSourceType.EnvironmentVariable,
                    SecretScope.Instance,
                    ScopeId: null,
                    DateTimeOffset.UtcNow))
                : SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedInspectionClient : IKeycloakAdminClient
    {
        public Task<KeycloakAdminInspectionResult> InspectAsync(
            KeycloakAdminInspectionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(KeycloakAdminInspectionResult.Success(
                KeycloakInspectionStatus.Inspected,
                new KeycloakInspectionSnapshot(
                    "operators",
                    "event-bff",
                    "event-api",
                    realmExists: true,
                    effectiveMappers:
                    [
                        new KeycloakEffectiveMapperSnapshot(
                            "native-subject",
                            KeycloakMapperSemantic.Subject,
                            KeycloakMapperOrigin.Native,
                            Audience: null,
                            AddsToAccessToken: true,
                            AddsToIdToken: true,
                            IsEffective: true)
                    ])));
    }

    public sealed class InMemoryOperationRepository
        : IKeycloakOperationRepository
    {
        public KeycloakOperation? Operation { get; private set; }
        private Guid _persistedStamp;

        public Task<KeycloakOperation?> GetAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Operation?.Id == id ? Operation : null);

        public Task AddAsync(
            KeycloakOperation operation,
            CancellationToken cancellationToken = default)
        {
            Operation = operation;
            _persistedStamp = operation.ConcurrencyStamp;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            KeycloakOperation operation,
            Guid expectedConcurrencyStamp,
            CancellationToken cancellationToken = default)
        {
            if (expectedConcurrencyStamp != _persistedStamp)
            {
                throw new InvalidOperationException(
                    "Unexpected receipt concurrency.");
            }

            Operation = operation;
            _persistedStamp = operation.ConcurrencyStamp;
            return Task.CompletedTask;
        }

        public Task<bool> HasUnresolvedOverlapAsync(
            KeycloakTarget target,
            Guid? excludedOperationId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<KeycloakOperation>>
            FindSettledRetentionEligibleAsync(
                DateTimeOffset beforeUtc,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeycloakOperation>>([]);

        public Task DeleteAsync(
            KeycloakOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryCoordinator(
        InMemoryOperationRepository repository)
        : IKeycloakOperationCoordinator
    {
        public Task<T> ExecuteAsync<T>(
            KeycloakOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            repository.Operation?.State != KeycloakOperationState.Applying
                ? throw new InvalidOperationException(
                    "Intent was not persisted before provider execution.")
                : action(cancellationToken);

        public Task RequestCancellationAsync(
            Guid operationId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default)
        {
            KeycloakOperation operation = repository.Operation
                ?? throw new KeyNotFoundException();
            Guid expectedStamp = operation.ConcurrencyStamp;
            operation.RequestCancellation(requestedAtUtc);
            return repository.SaveAsync(
                operation,
                expectedStamp,
                cancellationToken);
        }
    }

    private sealed class UnknownOutcomeAdminClient
        : IKeycloakAdminOperationClient
    {
        public int ApplyCount { get; private set; }

        public Task<KeycloakMapperOperationResult>
            ApplyApprovedMapperAsync(
                KeycloakMapperOperationRequest request,
                CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(new KeycloakMapperOperationResult(
                KeycloakStepOutcomeKind.OutcomeUnknown,
                "keycloak_mapper_outcome_unknown"));
        }

        public Task<KeycloakMapperOperationResult>
            InspectApprovedMapperAsync(
                KeycloakMapperOperationRequest request,
                CancellationToken cancellationToken) =>
            Task.FromResult(new KeycloakMapperOperationResult(
                KeycloakStepOutcomeKind.Verified,
                "keycloak_mapper_verified"));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
