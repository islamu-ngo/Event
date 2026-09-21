using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class CompletionGenerationRaceHttpTests
{
    [Test]
    [Arguments(AuthenticationProviderKind.Local, false)]
    [Arguments(AuthenticationProviderKind.Keycloak, false)]
    [Arguments(AuthenticationProviderKind.Local, true)]
    [Arguments(AuthenticationProviderKind.Keycloak, true)]
    public async Task ConcurrentProfileAndCompletionPreserveCommittedAuthority(AuthenticationProviderKind provider, bool completionFirst)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).Build();
        await postgres.StartAsync(token);
        var barrier = new BeforeCompletionBarrier();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: provider, incompleteSetup: true,
            postgreSqlConnectionString: postgres.GetConnectionString());
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "local", ["Keycloak:ClientId"] = "islamu-event-blazor",
                ["PublicBaseUrl"] = "https://example.test"
            }));
            builder.ConfigureTestServices(services =>
            {
                var descriptor = services.Single(service => service.ServiceType == typeof(IUnitOfWork));
                services.Remove(descriptor);
                services.AddScoped<IUnitOfWork>(provider => new GatedUnitOfWork(
                    (IUnitOfWork)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!), barrier));
            });
        });
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        if (provider == AuthenticationProviderKind.Keycloak)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.CreateExternalProviderToken(Guid.CreateVersion7(), "operator@example.test", true));
        var profile = new SelfHostOnboardingProfileDto { SiteName = "P0", CanonicalUrl = "https://example.test" };
        using var saved = await client.PatchAsJsonAsync("/api/instanceonboarding/profile", profile, token);
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var journeyResponse = await client.GetAsync("/api/instanceonboarding/journey", token);
        journeyResponse.EnsureSuccessStatusCode();
        using var journey = JsonDocument.Parse(await journeyResponse.Content.ReadAsStringAsync(token));
        await Assert.That(journey.RootElement.GetProperty("preflight").GetProperty("isReadyToLaunch").GetBoolean()).IsTrue();
        var settings = new CompleteInstanceOnboardingRequest
        {
            SiteProfile = profile, ExpectedJourneyGeneration = journey.RootElement.GetProperty("generation").GetString()
        };
        Task<HttpResponseMessage> CompleteAsync() => provider == AuthenticationProviderKind.Local
            ? client.PostAsJsonAsync("/api/instanceonboarding/complete-local", new CompleteLocalInstanceOnboardingRequestDto
            {
                OperationId = Guid.CreateVersion7(), Username = $"operator-{Guid.CreateVersion7():N}",
                TemporaryPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", Settings = settings
            }, token)
            : client.PostAsJsonAsync("/api/instanceonboarding/complete", settings, token);
        Task<HttpResponseMessage> ChangeProfileAsync() => client.PatchAsJsonAsync("/api/instanceonboarding/profile",
            profile with { SiteName = "P1" }, token);
        barrier.Arm();
        var delayed = completionFirst ? ChangeProfileAsync() : CompleteAsync();
        try
        {
            await barrier.Entered.Task.WaitAsync(token);
            Console.WriteLine(completionFirst
                ? "Profile P1 admitted with active setup; transaction not begun."
                : "Completion admitted G0/P0; transaction not begun.");
            using var committed = await (completionFirst ? CompleteAsync() : ChangeProfileAsync());
            await Assert.That(committed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Console.WriteLine(completionFirst
                ? "Completion P0 committed and setup locked before profile transaction."
                : "Profile P1 committed before completion transaction.");
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await delayed.WaitAsync(token);
        await using var database = factory.CreateDatabase();
        var name = await database.SystemSettings.AsNoTracking().SingleAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Branding.DisplayName, token);
        var persistedName = JsonSerializer.Deserialize<string>(name.Value!);
        var completed = await database.InstanceBootstrapStates.AnyAsync(state => state.Status == InstanceBootstrapStatus.Completed, token);
        var administrators = await database.PlatformUserRoles.CountAsync(token);
        var credentials = await database.LocalIdentityUsers.CountAsync(token);
        var receipts = await database.Set<Explore.Persistence.Identity.LocalIdentityCredentialOperation>().CountAsync(token);
        Console.WriteLine($"Readback: HTTP={(int)response.StatusCode}; profile={persistedName}; completed={completed}; administrators={administrators}; credentials={credentials}; receipts={receipts}");
        if (completionFirst)
        {
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(persistedName).IsEqualTo("P0");
            await Assert.That(completed).IsTrue();
            await Assert.That(administrators).IsEqualTo(1);
            return;
        }
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);
        await Assert.That(persistedName).IsEqualTo("P1");
        await Assert.That(completed).IsFalse();
        await Assert.That(administrators).IsEqualTo(0);
        await Assert.That(credentials).IsEqualTo(0);
        await Assert.That(receipts).IsEqualTo(0);
        using var pending = await client.GetAsync("/api/instanceonboarding/journey", token);
        await Assert.That(pending.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class BeforeCompletionBarrier
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public async Task WaitAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedUnitOfWork(IUnitOfWork inner, BeforeCompletionBarrier barrier) : IUnitOfWork
    {
        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
        {
            await barrier.WaitAsync(ct);
            await inner.ExecuteInTransactionAsync(operation, ct);
        }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await barrier.WaitAsync(ct);
            return await inner.ExecuteInTransactionAsync(operation, ct);
        }
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            inner.ExecuteReadCommittedAsync(operation, ct);
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await barrier.WaitAsync(ct);
            return await inner.ExecuteSerializableAsync(operation, ct);
        }
        public async Task<T> ExecuteBootstrapConvergenceAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await barrier.WaitAsync(ct);
            return await inner.ExecuteBootstrapConvergenceAsync(operation, ct);
        }
    }
}
