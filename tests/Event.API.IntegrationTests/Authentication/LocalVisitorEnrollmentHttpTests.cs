using System.Net;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel]
public sealed class LocalVisitorEnrollmentHttpTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LostPrimaryCommitResponseReconcilesOriginalCredentialWithoutAnotherAccount(
        IdentityDatabaseTopology topology)
    {
        var fault = new LostAdmissionCommitResponse();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: topology, selectedInterceptors: [fault, new AdmissionCommitCapture(fault)]);
        var factory = fixture.Native;
        var client = fixture.Client;
        client.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, RandomHex());
        var reservation = await ReserveAsync(client);
        var request = new LocalVisitorRegistrationRequestDto(reservation.OperationId,
            $"visitor-{Guid.CreateVersion7():N}", $"Aa1!{RandomHex()}", null, reservation.EnrollmentIntent);
        fault.OperationId = reservation.OperationId;
        using var uncertain = await client.PostAsJsonAsync("/api/auth/local/enrollment/register", request);
        await Assert.That(fault.Observed).IsTrue();
        await Assert.That((int)uncertain.StatusCode >= 500).IsTrue();
        await AssertPrivateAsync(uncertain);

        VisitorEnrollmentOperation admitted;
        await using (var database = factory.CreateDatabase())
        {
            database.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
            database.BindTenantLease();
            admitted = await database.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.OperationId);
            await Assert.That(admitted.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
            await Assert.That(await database.TenantUsers.CountAsync(row =>
                row.Id == admitted.TenantUserId && row.UserId == admitted.LocalSubjectId)).IsEqualTo(1);
        }
        var committedCounts = await CountsAsync(factory);
        string? originalHash;
        await using (var identity = factory.CreateIdentityDatabase())
            originalHash = await identity.Set<LocalIdentityUser>().Where(row => row.Id == admitted.LocalSubjectId)
                .Select(row => row.PasswordHash).SingleAsync();
        await Assert.That(string.IsNullOrEmpty(originalHash)).IsFalse();
        using var pendingLogin = await client.PostAsJsonAsync("/api/auth/local/login",
            new LocalAuthRequestDto(request.Username, request.Password));
        await Assert.That(pendingLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using var retry = await client.PostAsJsonAsync("/api/auth/local/enrollment/register", request);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(retry);
        await Assert.That(await CountsAsync(factory)).IsEqualTo(committedCounts);
        await using (var identity = factory.CreateIdentityDatabase())
        {
            string? settledHash = await identity.Set<LocalIdentityUser>().Where(row => row.Id == admitted.LocalSubjectId)
                .Select(row => row.PasswordHash).SingleAsync();
            await Assert.That(settledHash == originalHash).IsTrue();
        }
        await using (var database = factory.CreateDatabase())
        {
            database.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
            database.BindTenantLease();
            var active = await database.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == admitted.Id);
            await Assert.That(active.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
            await Assert.That(active.TenantUserId).IsEqualTo(admitted.TenantUserId);
            await Assert.That(active.CredentialOperationId).IsEqualTo(admitted.CredentialOperationId);
        }
        using var readyLogin = await client.PostAsJsonAsync("/api/auth/local/login",
            new LocalAuthRequestDto(request.Username, request.Password));
        await Assert.That(readyLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ScheduledRecoveryCancelsExpiredReservationAndAcknowledgesCleanupThroughProductionScopes()
    {
        var clock = new EnrollmentClock();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(timeProvider: clock);
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, RandomHex());
        var reservation = await ReserveAsync(client);
        var before = await CountsAsync(factory);
        clock.Advance(TimeSpan.FromMinutes(16));

        await using var scope = factory.Services.CreateAsyncScope();
        var job = ActivatorUtilities.CreateInstance<VisitorEnrollmentRecoveryJob>(scope.ServiceProvider);
        var execution = Substitute.For<IJobExecutionContext>();
        execution.CancellationToken.Returns(TestContext.Current!.Execution.CancellationToken);
        await job.Execute(execution);

        await using var database = factory.CreateDatabase();
        database.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
        database.BindTenantLease();
        var receipt = await database.Set<VisitorEnrollmentOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.OperationId);
        await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
        await Assert.That(receipt.CleanupCompletedAt).IsNotNull();
        await Assert.That(await CountsAsync(factory)).IsEqualTo(before);
        var next = await scope.ServiceProvider.GetRequiredService<ILocalVisitorEnrollmentRecoveryService>()
            .RecoverOnceAsync(execution.CancellationToken);
        await Assert.That(next).IsEqualTo(new VisitorEnrollmentRecoveryResult(0, 0));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticatedPolicyClosureRejectsNewEnrollmentAndCancelsUnadmittedReservation(bool reserved)
    {
        var failure = new RequestFailureMetadata();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(exceptionObserver: failure);
        using var visitor = Client(factory);
        visitor.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, RandomHex());
        var reservation = reserved ? await ReserveAsync(visitor) : null;
        using var administrator = await AdministratorAsync(factory);
        using var closed = await administrator.PutAsJsonAsync(
            "/api/settings/tenant/keys/" + GovernanceSettingKeys.Tenants.UserRegistrationEnabled,
            new { value = "false" });
        await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(failure.Source ?? await closed.Content.ReadAsStringAsync());
        var before = await CountsAsync(factory);
        string username = $"visitor-{Guid.CreateVersion7():N}";
        string password = $"Aa1!{RandomHex()}";
        using var response = reservation is null
            ? await visitor.PostAsJsonAsync("/api/auth/local/enrollment/reserve",
                new LocalVisitorEnrollmentReservationRequestDto(Guid.CreateVersion7()))
            : await visitor.PostAsJsonAsync("/api/auth/local/enrollment/register",
                new LocalVisitorRegistrationRequestDto(reservation.OperationId, username, password, null,
                    reservation.EnrollmentIntent));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict)
            .Because(await response.Content.ReadAsStringAsync());
        await AssertPrivateAsync(response);
        var after = await CountsAsync(factory);
        await Assert.That(after.Users).IsEqualTo(before.Users);
        await Assert.That(after.Actors).IsEqualTo(before.Actors);
        await Assert.That(after.Logins).IsEqualTo(before.Logins);
        await Assert.That(after.Reservations).IsEqualTo(before.Reservations);
        if (reservation is not null)
        {
            await using var database = factory.CreateDatabase();
            database.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
            database.BindTenantLease();
            var receipt = await database.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.OperationId);
            await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
            using var login = await visitor.PostAsJsonAsync("/api/auth/local/login",
                new LocalAuthRequestDto(username, password));
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task DefaultOpenEnrollmentCreatesUsableAccountWithoutEmailOrBrowserToken()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = Client(factory);
        string proof = RandomHex();
        client.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, proof);
        var reservation = await ReserveAsync(client);
        string password = $"Aa1!{RandomHex()}";
        var request = new LocalVisitorRegistrationRequestDto(reservation.OperationId,
            $"visitor-{Guid.CreateVersion7():N}", password, null, reservation.EnrollmentIntent);

        using var registration = await client.PostAsJsonAsync("/api/auth/local/enrollment/register", request);
        await Assert.That(registration.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(registration);
        string result = await registration.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(result);
        await Assert.That(json.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("refreshToken", out _)).IsFalse();
        await Assert.That(result.Contains(password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(proof, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(reservation.EnrollmentIntent, StringComparison.Ordinal)).IsFalse();

        using var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new LocalAuthRequestDto(request.Username, password));
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var authenticated = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        await Assert.That(authenticated.RootElement.GetProperty("emailVerified").GetBoolean()).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(
            authenticated.RootElement.GetProperty("token").GetString())).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedProofOrProtectedClaimCannotCreateAnAccount(bool changeClaim)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, RandomHex());
        var reservation = await ReserveAsync(client);
        var before = await CountsAsync(factory);
        string claim = reservation.EnrollmentIntent;
        if (changeClaim)
        {
            int middle = claim.Length / 2;
            claim = claim[..middle] + (claim[middle] == 'A' ? 'B' : 'A') + claim[(middle + 1)..];
        }
        else
        {
            client.DefaultRequestHeaders.Remove(LocalVisitorBrowserProof.HeaderName);
            client.DefaultRequestHeaders.Add(LocalVisitorBrowserProof.HeaderName, RandomHex());
        }
        using var response = await client.PostAsJsonAsync("/api/auth/local/enrollment/register",
            new LocalVisitorRegistrationRequestDto(reservation.OperationId,
                $"visitor-{Guid.CreateVersion7():N}", $"Aa1!{RandomHex()}", null, claim));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertPrivateAsync(response);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(await CountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task MissingBrowserProofCannotAllocateReservationOrAccount()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = Client(factory);
        var before = await CountsAsync(factory);
        using var response = await client.PostAsJsonAsync("/api/auth/local/enrollment/reserve",
            new LocalVisitorEnrollmentReservationRequestDto(Guid.CreateVersion7()));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertPrivateAsync(response);
        await Assert.That(await CountsAsync(factory)).IsEqualTo(before);
    }

    private static async Task<LocalVisitorEnrollmentReservation> ReserveAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/local/enrollment/reserve",
            new LocalVisitorEnrollmentReservationRequestDto(Guid.CreateVersion7()));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(response);
        return await response.Content.ReadFromJsonAsync<LocalVisitorEnrollmentReservation>()
            ?? throw new InvalidOperationException("The enrollment reservation is absent.");
    }

    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
    }

    private static async Task<(int Identities, int Users, int Actors, int Logins, int Reservations)> CountsAsync(
        LocalAdmissionWebApplicationFactory factory)
    {
        await using var context = factory.CreateDatabase();
        context.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
        context.BindTenantLease();
        return (await context.LocalIdentityUsers.CountAsync(), await context.Users.CountAsync(),
            await context.Actors.CountAsync(), await context.UserExternalLogins.CountAsync(),
            await context.Set<VisitorEnrollmentOperation>().CountAsync());
    }

    private static HttpClient Client(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<HttpClient> AdministratorAsync(LocalAdmissionWebApplicationFactory factory)
    {
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var identity = await database.LocalIdentityUsers.SingleAsync(row => row.Email == credentials.Identifier);
            var user = await database.Users.SingleAsync(row => row.Id == identity.Id);
            var tenant = await database.Tenants.SingleAsync();
            var actor = await database.Actors.SingleAsync(row => row.UserId == user.Id);
            var role = await database.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin");
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                RoleId = role.Id,
                Role = role,
                GrantedAt = DateTime.UtcNow
            });
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = user.Id,
                User = user,
                ActorId = actor.Id,
                Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active,
                CreatedAt = DateTime.UtcNow
            };
            database.TenantUsers.Add(member);
            database.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<LocalIdentityRole>>();
            await Assert.That((await roles.CreateAsync(new LocalIdentityRole("platform.admin"))).Succeeded).IsTrue();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            await Assert.That((await manager.AddToRoleAsync(identity, "platform.admin")).Succeeded).IsTrue();
        }
        var client = Client(factory);
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", "local-admission");
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var session = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            session.RootElement.GetProperty("token").GetString());
        return client;
    }

    private static string RandomHex() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private sealed class LostAdmissionCommitResponse : DbTransactionInterceptor
    {
        public Guid OperationId { get; set; }
        public bool Observed { get; private set; }
        public DbTransaction? AdmissionTransaction { get; set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && ReferenceEquals(transaction, AdmissionTransaction))
            {
                Observed = true;
                throw new IOException("The primary commit response was lost.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class AdmissionCommitCapture(LostAdmissionCommitResponse response) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (response.OperationId != Guid.Empty && eventData.Context is ExploreDbContext database
                && database.ChangeTracker.Entries<VisitorEnrollmentOperation>().Any(entry =>
                    entry.Entity.Id == response.OperationId && entry.Entity.Stage == VisitorEnrollmentStage.PrimaryAdmitted))
                response.AdmissionTransaction = database.Database.CurrentTransaction?.GetDbTransaction()
                    ?? throw new InvalidOperationException("Admission lost its original physical transaction.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EnrollmentClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed class RequestFailureMetadata : IExceptionHandler
    {
        public string? Source { get; private set; }
        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            Source = $"{exception.GetType().FullName}{Environment.NewLine}{exception.StackTrace}";
            return ValueTask.FromResult(false);
        }
    }
}
