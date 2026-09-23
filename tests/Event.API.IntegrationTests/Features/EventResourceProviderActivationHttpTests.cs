using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceProviderActivationHttpTests
{
    private const string BindingsPath = "/api/event-resource-provider-activation/bindings";
    private const string BeginPath = "/api/event-resource-provider-activation/begin";
    private const string ActivatePath = "/api/event-resource-provider-activation/activate";
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NullOrOmittedEndpointCollectionsReturnBadRequest(bool omit)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await GrantAdministratorAsync(factory, credentials.Identifier);
        using var client = CreateClient(factory);
        await AuthenticateAsync(client, credentials);
        Guid deploymentId = Guid.CreateVersion7();
        object binding = omit
            ? new { deploymentId, scope = "", policyVersion = "default" }
            : new { deploymentId, endpoints = (string[]?)null, scope = "", policyVersion = "default" };
        using var response = await client.PutAsJsonAsync(BindingsPath,
            new { binding, expectedRevision = Guid.Empty }, Token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnonymousAndNonAdministratorRequestsAreDeniedWithPrivateNoStore()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var anonymous = CreateClient(factory);

        using (HttpResponseMessage read = await anonymous.GetAsync(BindingsPath, Token))
            await AssertDeniedAsync(read, HttpStatusCode.Forbidden);
        using (HttpResponseMessage write = await anonymous.PostAsJsonAsync(BeginPath,
                   new { deploymentId = Guid.CreateVersion7() }, Token))
            await AssertDeniedAsync(write, HttpStatusCode.Unauthorized);

        LocalAuthRequestDto credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        using var ordinary = CreateClient(factory);
        await AuthenticateAsync(ordinary, credentials);
        using (HttpResponseMessage read = await ordinary.GetAsync(BindingsPath, Token))
            await AssertDeniedAsync(read, HttpStatusCode.Forbidden);
        using (HttpResponseMessage write = await ordinary.PostAsJsonAsync(BeginPath,
                   new { deploymentId = Guid.CreateVersion7() }, Token))
            await AssertDeniedAsync(write, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AdministratorCanDriveThePersistedActivationProtocolAndRevocationIsFresh()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        LocalAuthRequestDto credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid administratorId = await GrantAdministratorAsync(factory, credentials.Identifier);
        using var client = CreateClient(factory);
        await AuthenticateAsync(client, credentials);

        Guid emptyRevision;
        using (HttpResponseMessage initial = await client.GetAsync(BindingsPath, Token))
        {
            await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateNoStoreAsync(initial);
            using JsonDocument body = await JsonDocument.ParseAsync(await initial.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            emptyRevision = body.RootElement.GetProperty("revision").GetGuid();
            await Assert.That(emptyRevision).IsEqualTo(Guid.Empty);
            await Assert.That(body.RootElement.GetProperty("deployments").GetArrayLength()).IsEqualTo(0);
        }

        Guid deploymentId = Guid.CreateVersion7();
        string[] aliases = ["https://pdp.example.test", "https://alias.example.test"];
        string bindingReplayKey = $"resource-binding-{Guid.CreateVersion7():N}";
        client.DefaultRequestHeaders.Add("Idempotency-Key", bindingReplayKey);
        EventResourceProviderOperation bound;
        using (HttpResponseMessage response = await client.PutAsJsonAsync(BindingsPath, new
               {
                   binding = new { deploymentId, endpoints = aliases, scope = "", policyVersion = "default" },
                   expectedRevision = emptyRevision
               }, Token))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await response.Content.ReadAsStringAsync(Token));
            bound = (await response.Content.ReadFromJsonAsync<EventResourceProviderOperation>(Token))!;
            await Assert.That(bound.DeploymentId).IsEqualTo(deploymentId);
            await Assert.That(bound.Epoch).IsEqualTo(1);
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        using (HttpResponseMessage incompatible = await ActivateAsync(client, bound,
                   previousWritersStopped: true, reachable: 2, declared: 2, frozenParentPolicyContractConfirmed: false))
            await AssertProblemAsync(incompatible, HttpStatusCode.Conflict);

        using (HttpResponseMessage read = await client.GetAsync(BindingsPath, Token))
        {
            using JsonDocument body = await JsonDocument.ParseAsync(await read.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            JsonElement binding = body.RootElement.GetProperty("deployments")[0];
            await Assert.That(binding.GetProperty("scope").GetString()).IsEqualTo(string.Empty);
            await Assert.That(binding.GetProperty("endpoints").EnumerateArray().Select(value => value.GetString()!).ToArray())
                .IsEquivalentTo(aliases);
        }

        client.DefaultRequestHeaders.Add("Idempotency-Key", bindingReplayKey);
        using (HttpResponseMessage staleRevision = await client.PutAsJsonAsync(BindingsPath, new
               {
                   binding = new { deploymentId, endpoints = aliases, scope = "", policyVersion = "default" },
                   expectedRevision = emptyRevision
               }, Token))
            await AssertProblemAsync(staleRevision, HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        using (HttpResponseMessage malformed = await client.PutAsJsonAsync(BindingsPath, new
               {
                   binding = new { deploymentId = Guid.NewGuid(), endpoints = new[] { "not-a-route" }, scope = "", policyVersion = "default" },
                   expectedRevision = bound.BindingRevision
               }, Token))
            await AssertProblemAsync(malformed, HttpStatusCode.BadRequest);
        using (HttpResponseMessage unknown = await client.PostAsJsonAsync(BeginPath,
                   new { deploymentId = Guid.CreateVersion7() }, Token))
            await AssertProblemAsync(unknown, HttpStatusCode.BadRequest);

        EventResourceProviderOperation current;
        string beginReplayKey = $"resource-activation-{Guid.CreateVersion7():N}";
        client.DefaultRequestHeaders.Add("Idempotency-Key", beginReplayKey);
        using (HttpResponseMessage begin = await client.PostAsJsonAsync(BeginPath, new { deploymentId }, Token))
        {
            await Assert.That(begin.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await begin.Content.ReadAsStringAsync(Token));
            current = (await begin.Content.ReadFromJsonAsync<EventResourceProviderOperation>(Token))!;
            await Assert.That(current.Epoch).IsEqualTo(bound.Epoch + 1);
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        using (HttpResponseMessage stale = await ActivateAsync(client, bound, previousWritersStopped: true, reachable: 2, declared: 2))
            await AssertProblemAsync(stale, HttpStatusCode.Conflict);
        using (HttpResponseMessage staleOperation = await ActivateAsync(client,
                   current with { OperationId = Guid.CreateVersion7() }, previousWritersStopped: true, reachable: 2, declared: 2))
            await AssertProblemAsync(staleOperation, HttpStatusCode.Conflict);
        using (HttpResponseMessage staleEpoch = await ActivateAsync(client,
                   current with { Epoch = current.Epoch - 1 }, previousWritersStopped: true, reachable: 2, declared: 2))
            await AssertProblemAsync(staleEpoch, HttpStatusCode.Conflict);
        using (HttpResponseMessage incomplete = await ActivateAsync(client, current, previousWritersStopped: true, reachable: 2, declared: 1))
            await AssertProblemAsync(incomplete, HttpStatusCode.Conflict);
        using (HttpResponseMessage retry = await ActivateAsync(client, current, previousWritersStopped: true, reachable: 2, declared: 2))
            await AssertProblemAsync(retry, HttpStatusCode.Conflict);

        EventResourceProviderOperation recovery;
        using (HttpResponseMessage begin = await client.PostAsJsonAsync(BeginPath, new { deploymentId }, Token))
        {
            begin.EnsureSuccessStatusCode();
            recovery = (await begin.Content.ReadFromJsonAsync<EventResourceProviderOperation>(Token))!;
        }
        string activationReplayKey = $"resource-activation-confirmation-{Guid.CreateVersion7():N}";
        client.DefaultRequestHeaders.Add("Idempotency-Key", activationReplayKey);
        using (HttpResponseMessage activated = await ActivateAsync(client, recovery, previousWritersStopped: true, reachable: 2, declared: 2))
            await Assert.That(activated.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        EventResourceProviderOperation newer;
        using (HttpResponseMessage begin = await client.PostAsJsonAsync(BeginPath, new { deploymentId }, Token))
        {
            begin.EnsureSuccessStatusCode();
            newer = (await begin.Content.ReadFromJsonAsync<EventResourceProviderOperation>(Token))!;
        }
        client.DefaultRequestHeaders.Add("Idempotency-Key", activationReplayKey);
        using (HttpResponseMessage replay = await ActivateAsync(client, recovery, previousWritersStopped: true, reachable: 2, declared: 2))
            await AssertProblemAsync(replay, HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        recovery = newer;
        using (HttpResponseMessage activated = await ActivateAsync(client, recovery, previousWritersStopped: true, reachable: 2, declared: 2))
            await Assert.That(activated.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await using (var database = factory.CreateDatabase())
        {
            EventResourceProviderActivation persisted = await database.EventResourceProviderActivations
                .AsNoTracking().SingleAsync(row => row.Id == deploymentId, Token);
            await Assert.That(persisted.State).IsEqualTo(EventResourceProviderActivationStateEnum.Active);
            await Assert.That(persisted.Epoch).IsEqualTo(recovery.Epoch);
            database.PlatformUserRoles.RemoveRange(database.PlatformUserRoles.Where(grant => grant.UserId == administratorId));
            await database.SaveChangesAsync(Token);
        }

        client.DefaultRequestHeaders.Add("Idempotency-Key", beginReplayKey);
        using (HttpResponseMessage revoked = await client.PostAsJsonAsync(BeginPath, new { deploymentId }, Token))
            await AssertDeniedAsync(revoked, HttpStatusCode.Forbidden);
        await using (var database = factory.CreateDatabase())
        {
            EventResourceProviderActivation persisted = await database.EventResourceProviderActivations
                .AsNoTracking().SingleAsync(row => row.Id == deploymentId, Token);
            await Assert.That(persisted.State).IsEqualTo(EventResourceProviderActivationStateEnum.Active);
            await Assert.That(persisted.Epoch).IsEqualTo(recovery.Epoch);
        }
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<Guid> GrantAdministratorAsync(LocalAdmissionWebApplicationFactory factory, string email)
    {
        await using var database = factory.CreateDatabase();
        User user = await database.Users.SingleAsync(row => row.Pii!.Email == email, Token);
        database.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = user.Id
        });
        await database.SaveChangesAsync(Token);
        return user.Id;
    }

    private static async Task AuthenticateAsync(HttpClient client, LocalAuthRequestDto credentials)
    {
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        login.EnsureSuccessStatusCode();
        using JsonDocument body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
    }

    private static Task<HttpResponseMessage> ActivateAsync(HttpClient client, EventResourceProviderOperation operation,
        bool previousWritersStopped, int reachable, int declared, bool frozenParentPolicyContractConfirmed = true) =>
        client.PostAsJsonAsync(ActivatePath, new
        {
            deploymentId = operation.DeploymentId,
            operationId = operation.OperationId,
            epoch = operation.Epoch,
            scope = "",
            policyVersion = "default",
            previousWritersStopped,
            frozenParentPolicyContractConfirmed,
            reachableReplicaCount = reachable,
            declaredPolicyReplicaCount = declared
        }, Token);

    private static async Task AssertDeniedAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync(Token));
        await AssertPrivateNoStoreAsync(response);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        string payload = await response.Content.ReadAsStringAsync(Token);
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(payload);
        await Assert.That(payload.Length).IsLessThan(4096);
        using JsonDocument problem = JsonDocument.Parse(payload);
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)expected);
        await AssertPrivateNoStoreAsync(response);
    }

    private static async Task AssertPrivateNoStoreAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }
}
