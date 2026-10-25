using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Event.Standalone.Hosting;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Testing.DataProtection;
using Event.Testing.Hosting;
using Explore.API.Hosting;
using Explore.API.Controllers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Event.Standalone.IntegrationTests;

// The deployment fixture selects and restores process environment secret authority.
[NotInParallel]
public sealed class PrivacyErasureStartupRecoveryTests
{
    private static readonly TimeSpan StartupDeadline = TimeSpan.FromSeconds(30);

    [Test]
    [Timeout(180_000)]
    public async Task ExistingGlobalCustody_IsBorrowedAndConflictingPurposeIsRejected()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        CancellationToken cancellationToken = deadline.Token;
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        ConfigureRetainedAuthority(deployment);
        var observation = new BorrowedOwnerObservation();
        await using var native = deployment.CreateHost();
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivacyErasureReplayCheckpointRepository>();
            services.AddScoped<IPrivacyErasureReplayCheckpointRepository>(provider =>
            {
                var database = provider.GetRequiredService<ExploreDbContext>();
                return new ObservedCheckpointRepository(new PrivacyErasureReplayCheckpointRepository(database),
                    database, observation);
            });
        }));
        using HttpClient client = host.CreateClient(ClientOptions());
        await using (AsyncServiceScope parent = host.Services.CreateAsyncScope())
        await using (IPolicyMutationExecutionScope selected = await parent.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, cancellationToken))
        {
            var database = selected.Services.GetRequiredService<ExploreDbContext>();
            var human = selected.Services.GetRequiredService<IHumanSecuritySourceGate>();
            var discovery = selected.Services.GetRequiredService<IEventDiscoveryEnrollmentGate>();
            await using IAsyncDisposable humanLease = await human.AcquireAsync(cancellationToken);
            await using IAsyncDisposable discoveryLease = await discovery.AcquireAsync(cancellationToken);
            observation.Expected = database;
            await selected.Services.GetRequiredService<IPrivacyErasureService>().ReplayPendingAsync(cancellationToken);
            await Assert.That(observation.ObservedOriginal).IsTrue();
            human.RequireCurrent();
            discovery.RequireCurrent();
            await Assert.That(database.Database.CurrentTransaction).IsNull();
            observation.Expected = null;
        }
        await using (AsyncServiceScope parent = host.Services.CreateAsyncScope())
        await using (IPolicyMutationExecutionScope selected = await parent.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.EmailInstance, cancellationToken))
        {
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await selected.Services.GetRequiredService<IPrivacyErasureService>().ReplayPendingAsync(cancellationToken));
            await Assert.That(failure!.Message)
                .IsEqualTo("A policy operation cannot change its owning database authority.");
            await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        }
        await AssertNoErasureAsync(host.Services, deployment.DatabasePath, cancellationToken);
    }

    [Test]
    [Timeout(180_000)]
    public async Task LocalNonAdministrator_CannotEraseAnotherUser_AndCanEraseSelf()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        CancellationToken cancellationToken = deadline.Token;
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        ConfigureRetainedAuthority(deployment);
        var probe = new ForeignErasureProbe();
        SeededSubjects subjects;
        string actorBearer;
        await using (var native = deployment.CreateHost())
        await using (var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new ForeignErasureFilter(probe))))))
        {
            using HttpClient administrator = host.CreateClient(ClientOptions());
            string administratorBearer = await CompleteFirstUseAndLoginAsync(administrator, deployment.Subject,
                deployment.InitialPassword, cancellationToken);
            administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administratorBearer);
            await ActivateDirectoryAsync(administrator, cancellationToken);
            (Guid actor, string bearer) = await CreateOrdinaryLocalAsync(host, administrator, cancellationToken);
            (Guid unrelated, _) = await CreateOrdinaryLocalAsync(host, administrator, cancellationToken);
            actorBearer = bearer;
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
                var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                Location home = NewHome(actor);
                Location unrelatedHome = NewHome(unrelated);
                database.AddRange(home, unrelatedHome);
                await database.SaveChangesAsync(cancellationToken);
                subjects = new(actor, unrelated, home.Id, unrelatedHome.Id);
            }
            await AssertOrdinaryLocalAsync(deployment.DatabasePath, actor, cancellationToken);
            await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, null, cancellationToken));
            await AssertNoErasureAsync(host.Services, deployment.DatabasePath, cancellationToken);

            probe.Target = unrelated;
            probe.Intent = Guid.CreateVersion7();
            using HttpClient ordinary = host.CreateClient(ClientOptions());
            ordinary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", actorBearer);
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/User");
            request.Headers.Add("Idempotency-Key", probe.Intent.ToString("D"));
            using HttpResponseMessage denied = await ordinary.SendAsync(request, cancellationToken);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(probe.Authenticated && probe.InvokedTarget == unrelated && probe.NativeDenied).IsTrue();
            await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, null, cancellationToken));
            await AssertNoErasureAsync(host.Services, deployment.DatabasePath, cancellationToken);
        }

        // A normal host without the CQS probe exercises the unchanged self-only HTTP route.
        await using (var host = deployment.CreateHost())
        {
            using HttpClient ordinary = host.OpenClient();
            ordinary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", actorBearer);
            await AssertOrdinaryLocalAsync(deployment.DatabasePath, subjects.ErasedUser, cancellationToken);
            await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, null, cancellationToken));
            Guid intentId = Guid.CreateVersion7();
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/User");
            request.Headers.Add("Idempotency-Key", intentId.ToString("D"));
            using HttpResponseMessage accepted = await ordinary.SendAsync(request, cancellationToken);
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            JsonElement start = await ReadJsonAsync(accepted, cancellationToken);
            string receipt = start.GetProperty("receipt").GetString()!;
            PrivacyErasureIntent retained;
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
                retained = (await scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>()
                    .ReadAfterAsync(0, 100, cancellationToken)).Single();
            await Assert.That(retained.IntentId == intentId && retained.SubjectId == subjects.ErasedUser).IsTrue();
            await AssertErasedAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));

            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/privacy-erasure/status");
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("ErasureReceipt", receipt);
            using HttpResponseMessage status = await ordinary.SendAsync(statusRequest, cancellationToken);
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        ClearPrimaryPool(deployment.DatabasePath);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task RetainedErasure_CommitsBeforeServerStartupAfterPrimaryOnlyRestore(bool standalone)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        CancellationToken cancellationToken = deadline.Token;
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        string directory = Path.GetDirectoryName(deployment.DatabasePath)!;
        string backup = Path.Combine(directory, "primary-before-erasure.db");
        ConfigureRetainedAuthority(deployment);

        SeededSubjects subjects;
        PrivacyErasureIntent retained;
        int settledOutboxCount;
        await using (var host = deployment.CreateHost())
        {
            using HttpClient client = host.OpenClient();
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                    .SetTenant(PlatformDefaults.DefaultTenantId);
                subjects = await SeedAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), cancellationToken);
            }
            await CopyDatabaseAsync(deployment.DatabasePath, backup, cancellationToken);
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IPrivacyErasureService>()
                    .EraseUserAsync(subjects.ErasedUser, Guid.CreateVersion7(), cancellationToken);
                retained = (await scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>()
                    .ReadAfterAsync(0, 100, cancellationToken)).Single();
            }
            StoredState settled = await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken);
            await AssertErasedAsync(settled);
            settledOutboxCount = settled.OutboxCount;
            await Assert.That(settledOutboxCount).IsGreaterThan(0);
        }

        ClearPrimaryPool(deployment.DatabasePath);
        await CopyDatabaseAsync(backup, deployment.DatabasePath, cancellationToken);
        await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));

        if (standalone)
            await ExerciseStartupAsync<StandaloneHostMarker>(deployment, subjects, retained, settledOutboxCount, cancellationToken);
        else
            await ExerciseStartupAsync<ApiHostCompositionState>(deployment, subjects, retained, settledOutboxCount, cancellationToken);
        ClearPrimaryPool(deployment.DatabasePath);
    }

    [Test]
    [Timeout(180_000)]
    public async Task SavedCheckpointFault_RollsBackErasureAndReplaysTheDurableIntent()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        CancellationToken cancellationToken = deadline.Token;
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        ConfigureRetainedAuthority(deployment);
        var fault = new CheckpointAppendFault();
        SeededSubjects subjects;
        SeededSessions sessions;
        PrivacyErasureIntent retained;
        CommittedEffects converged;
        Guid intentId = Guid.CreateVersion7();

        await using (var native = deployment.CreateHost())
        await using (var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivacyErasureReplayCheckpointRepository>();
            services.AddScoped<IPrivacyErasureReplayCheckpointRepository>(provider =>
            {
                var database = provider.GetRequiredService<ExploreDbContext>();
                return new FaultedCheckpointRepository(new PrivacyErasureReplayCheckpointRepository(database), database, fault);
            });
        })))
        {
            using HttpClient client = host.CreateClient(ClientOptions());
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                    .SetTenant(PlatformDefaults.DefaultTenantId);
                subjects = await SeedAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), cancellationToken);
            }

            sessions = await SeedEncryptedSessionsAsync(host.Services, subjects, cancellationToken);
            await AssertStoredSessionsAsync(deployment.DatabasePath, sessions, erased: false, cancellationToken);

            fault.Arm();
            InvalidOperationException? failure;
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IPrivacyErasureService>()
                        .EraseUserAsync(subjects.ErasedUser, intentId, cancellationToken);
                });
            }
            await Assert.That(failure!.Message).IsEqualTo(CheckpointAppendFault.FailureCode);
            await Assert.That(fault.ReachedAfterSave).IsTrue();

            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                retained = (await scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>()
                    .ReadAfterAsync(0, 100, cancellationToken)).Single();
            }
            await Assert.That(retained.IntentId == intentId && retained.SubjectId == subjects.ErasedUser).IsTrue();
            await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));
            await AssertStoredSessionsAsync(deployment.DatabasePath, sessions, erased: false, cancellationToken);
            await using (ExploreDbContext observer = OpenPrimary(deployment.DatabasePath))
            {
                await Assert.That(await observer.PrivacyErasurePolicyCoverage.CountAsync(cancellationToken)).IsEqualTo(0);
                PrivacyErasureSaga saga = await observer.PrivacyErasureSagas.AsNoTracking().SingleAsync(cancellationToken);
                await Assert.That(saga.IntentId == retained.IntentId && saga.SubjectId == subjects.ErasedUser
                    && saga.FenceToken == retained.AuthoritySequence).IsTrue();
                await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.Fenced);
                await Assert.That(saga.LocalSettledAtUtc).IsNull();
            }
            // Intent/fence survival is deliberate. The fault follows native checkpoint SaveChanges;
            // coverage, saga settlement and outbox insertion have not yet run in that transaction.
            await AssertAuthorityUnchangedAsync(host.Services, retained, cancellationToken);

            fault.Disarm();
            await ReplayFromEntryAsync(host.Services, cancellationToken);
            await AssertErasedAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));
            await AssertStoredSessionsAsync(deployment.DatabasePath, sessions, erased: true, cancellationToken);
            converged = await ReadCommittedEffectsAsync(deployment.DatabasePath, retained, cancellationToken);
            await ReplayFromEntryAsync(host.Services, cancellationToken);
            await AssertErasedAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));
            await AssertStoredSessionsAsync(deployment.DatabasePath, sessions, erased: true, cancellationToken);
            await AssertSameEffectsAsync(converged,
                await ReadCommittedEffectsAsync(deployment.DatabasePath, retained, cancellationToken));
            await AssertAuthorityUnchangedAsync(host.Services, retained, cancellationToken);
        }

        await using (var restarted = deployment.CreateHost())
        {
            using HttpClient client = restarted.OpenClient();
            await AssertErasedAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));
            await AssertStoredSessionsAsync(deployment.DatabasePath, sessions, erased: true, cancellationToken);
            await AssertSameEffectsAsync(converged,
                await ReadCommittedEffectsAsync(deployment.DatabasePath, retained, cancellationToken));
            await AssertAuthorityUnchangedAsync(restarted.Services, retained, cancellationToken);
        }
        ClearPrimaryPool(deployment.DatabasePath);
    }

    private static void ConfigureRetainedAuthority(NativeEmailOptionalStandaloneFixture deployment)
    {
        string directory = Path.GetDirectoryName(deployment.DatabasePath)!;
        deployment.SetEnvironment("PrivacyErasure__Authority__Topology", "EmbeddedSqlite");
        deployment.SetEnvironment("PrivacyErasureAuthorityEmbedded__Path", Path.Combine(directory, "authority", "retained.db"));
        deployment.SetEnvironment("PrivacyErasureAuthorityEmbedded__WriterReplicaCount", "1");
        deployment.SetEnvironment("WebhookDeliveryProcessor__Enabled", "false");
        deployment.SetEnvironment("IntegrationSyncProcessor__Enabled", "false");
        deployment.SetEnvironment("Atproto__PdsSync__Enabled", "false");
        BindApiDataProtection(deployment);
    }

    private static async Task<(Guid User, string Bearer)> CreateOrdinaryLocalAsync(
        WebApplicationFactory<StandaloneHostMarker> host, HttpClient administrator, CancellationToken cancellationToken)
    {
        using HttpResponseMessage created = await administrator.PostAsJsonAsync("/api/instance/local-identities", new
        {
            operationId = Guid.CreateVersion7(), email = $"privacy-{Guid.CreateVersion7():N}@example.test",
            firstName = "Synthetic", lastName = "Ordinary"
        }, cancellationToken);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        JsonElement issue = await ReadJsonAsync(created, cancellationToken);
        Guid subject = issue.GetProperty("operation").GetProperty("receipt").GetProperty("localSubjectId").GetGuid();
        string temporary = issue.GetProperty("temporaryPassword").GetString()!;
        using HttpClient client = host.CreateClient(ClientOptions());
        return (subject, await CompleteFirstUseAndLoginAsync(client, subject, temporary, cancellationToken));
    }

    private static async Task ActivateDirectoryAsync(HttpClient administrator, CancellationToken cancellationToken)
    {
        const string identityPath = "/api/tenant/settings/documents/directory-operator-identity";
        using HttpResponseMessage identity = await administrator.GetAsync(identityPath, cancellationToken);
        await Assert.That(identity.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement document = await ReadJsonAsync(identity, cancellationToken);
        using HttpResponseMessage saved = await administrator.PatchAsJsonAsync(identityPath,
            new PatchTenantDirectoryOperatorIdentityDocumentDto
            {
                ExpectedConcurrencyStamp = document.GetProperty("concurrencyStamp").GetGuid(),
                LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto
                {
                    PublicName = OptionalUpdate<string?>.Set("Synthetic privacy directory"),
                    LegalName = OptionalUpdate<string?>.Set("Synthetic Directory ASBL"),
                    OperatorKindCode = OptionalUpdate<string?>.Set("registered_organization"),
                    JurisdictionCountryCode = OptionalUpdate<string?>.Set("BE")
                },
                Contacts = new PatchTenantDirectoryOperatorContactsDto
                {
                    PublicContactEmail = OptionalUpdate<string?>.Set("contact@standalone.example.test")
                },
                LegalLinks = new PatchTenantDirectoryOperatorLegalLinksDto
                {
                    LegalNoticeUrl = OptionalUpdate<string?>.Set("https://standalone.example.test/legal"),
                    PrivacyUrl = OptionalUpdate<string?>.Set("https://standalone.example.test/privacy")
                }
            }, cancellationToken);
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadJsonAsync(saved, cancellationToken)).GetProperty("isActivationReady").GetBoolean()).IsTrue();
        using HttpResponseMessage activated = await administrator.PostAsJsonAsync(
            $"/api/admin/instance/tenants/{PlatformDefaults.DefaultTenantId}/activate", new { }, cancellationToken);
        await Assert.That(activated.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<string> CompleteFirstUseAndLoginAsync(
        HttpClient client, Guid subject, string temporaryPassword, CancellationToken cancellationToken)
    {
        using HttpResponseMessage initial = await client.PostAsJsonAsync("/api/auth/local/login", new
        {
            identifier = subject.ToString("D"), password = temporaryPassword
        }, cancellationToken);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement challenge = await ReadJsonAsync(initial, cancellationToken);
        await Assert.That(challenge.TryGetProperty("token", out _)).IsFalse();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            challenge.GetProperty("replacementChallenge").GetProperty("token").GetString());
        string password = NativeEmailOptionalStandaloneFixture.NewPassword();
        using (HttpResponseMessage replaced = await client.PostAsJsonAsync("/api/auth/local/credential-replacement",
            new { newPassword = password }, cancellationToken))
            await Assert.That(replaced.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage normal = await client.PostAsJsonAsync("/api/auth/local/login", new
        {
            identifier = subject.ToString("D"), password
        }, cancellationToken);
        await Assert.That(normal.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement session = await ReadJsonAsync(normal, cancellationToken);
        await Assert.That(session.TryGetProperty("replacementChallenge", out _)).IsFalse();
        return session.GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static async Task AssertOrdinaryLocalAsync(string path, Guid actor, CancellationToken cancellationToken)
    {
        await using ExploreDbContext observer = OpenPrimary(path);
        await Assert.That(await observer.PlatformUserRoles.AsNoTracking()
            .AnyAsync(value => value.UserId == actor && value.RoleId == (int)RoleEnum.Admin, cancellationToken)).IsFalse();
        await Assert.That(await observer.TenantUserRoleGrants.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(value => value.TenantUser.UserId == actor && value.RoleId == (int)RoleEnum.TenantAdmin
                && value.RevokedAt == null, cancellationToken)).IsFalse();
    }

    private static async Task AssertNoErasureAsync(IServiceProvider services, string path, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>()
            .ReadAfterAsync(0, 100, cancellationToken)).Count).IsEqualTo(0);
        await using ExploreDbContext observer = OpenPrimary(path);
        await Assert.That(await observer.PrivacyErasureSagas.CountAsync(cancellationToken)).IsEqualTo(0);
        await Assert.That(await observer.PrivacyErasureReplayCheckpoints.CountAsync(cancellationToken)).IsEqualTo(0);
    }

    private static async Task ReplayFromEntryAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPrivacyErasureService>().ReplayPendingAsync(cancellationToken);
    }

    private static async Task<SeededSessions> SeedEncryptedSessionsAsync(IServiceProvider services,
        SeededSubjects subjects, CancellationToken cancellationToken)
    {
        Guid secondTenant = Guid.CreateVersion7();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await using (IPolicyMutationExecutionScope selected = await scope.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, cancellationToken))
        {
            var seed = selected.Services.GetRequiredService<ExploreDbContext>();
            var humanGate = selected.Services.GetRequiredService<IHumanSecuritySourceGate>();
            var discoveryGate = selected.Services.GetRequiredService<IEventDiscoveryEnrollmentGate>();
            await using IAsyncDisposable human = await humanGate.AcquireAsync(cancellationToken);
            await using IAsyncDisposable discovery = await discoveryGate.AcquireAsync(cancellationToken);
            seed.Tenants.Add(new Tenant
            {
                Id = secondTenant, Slug = $"privacy-session-{secondTenant:N}", FullName = "Synthetic second tenant",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            });
            await seed.SaveChangesAsync(cancellationToken);
        }
        var snapshots = new List<SessionSnapshot>();
        foreach (var binding in new[]
        {
            (Tenant: PlatformDefaults.DefaultTenantId, User: subjects.ErasedUser),
            (Tenant: secondTenant, User: subjects.ErasedUser),
            (Tenant: PlatformDefaults.DefaultTenantId, User: subjects.UnrelatedUser)
        })
        {
            await using ExploreDbContext tenant = await scope.ServiceProvider.GetRequiredService<ITenantExploreDbContextFactory>()
                .CreateDbContextAsync(binding.Tenant, cancellationToken);
            string did = "did:plc:" + Guid.CreateVersion7().ToString("N")[..24];
            using var dpop = DPoPKeyPair.Generate();
            var session = new OAuthSessionData
            {
                DPoPKey = dpop.ExportKeyPair(), AuthMethod = "private_key_jwt",
                ClientId = "https://privacy.example.test/oauth/client-metadata.json",
                RedirectUri = "https://privacy.example.test/signin-atproto", Scope = "atproto transition:generic",
                TokenSet = new TokenSet
                {
                    Issuer = "https://issuer.example.test/", Sub = did, Audience = "https://pds.example.invalid/",
                    Scope = "atproto transition:generic", AccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                }
            };
            var repository = new UserAuthenticationTokenRepository(tenant);
            var store = new RepositoryBackedOAuthSessionStore(repository,
                new AtprotoSessionEnvelopeProtector(new SyntheticSessionSecrets()),
                new AtprotoOAuthSessionStoreContext(binding.Tenant, binding.User, AtprotoDid.Parse(did),
                    new Uri("https://pds.example.invalid/"), "privacy-client-key"));
            await store.StoreAsync(did, session, cancellationToken);
            OAuthSessionData? restored = await store.GetAsync(did, cancellationToken);
            await Assert.That(restored is not null && restored.TokenSet.AccessToken == session.TokenSet.AccessToken
                && restored.TokenSet.RefreshToken == session.TokenSet.RefreshToken).IsTrue();
            UserAuthenticationToken row = (await repository.GetAtprotoSessionForReadAsync(
                binding.Tenant, binding.User, "atproto", did, cancellationToken))!;
            snapshots.Add(new(row.Id, binding.Tenant, binding.User, row.SessionCiphertext.ToArray()));
        }
        await Assert.That(snapshots.Where(row => row.User == subjects.ErasedUser)
            .Select(row => row.Tenant).Distinct().Count()).IsEqualTo(2);
        return new(subjects.ErasedUser, snapshots.ToArray());
    }

    private static async Task AssertStoredSessionsAsync(string path, SeededSessions expected,
        bool erased, CancellationToken cancellationToken)
    {
        await using ExploreDbContext observer = OpenPrimary(path);
        Guid unrelated = expected.Rows.Where(row => row.User != expected.Subject).Select(row => row.User).Single();
        UserAuthenticationToken[] actual = await observer.UserAuthenticationTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.UserId == expected.Subject || row.UserId == unrelated).ToArrayAsync(cancellationToken);
        await Assert.That(actual.Count(row => row.UserId == expected.Subject)).IsEqualTo(erased ? 0 : 2);
        await Assert.That(actual.Count(row => row.UserId != expected.Subject)).IsEqualTo(1);
        await Assert.That(actual.All(row => expected.Rows.Any(original => original.Id == row.Id
            && original.Tenant == row.TenantId && original.User == row.UserId
            && original.Ciphertext.SequenceEqual(row.SessionCiphertext)))).IsTrue();
    }

    private static void BindApiDataProtection(NativeEmailOptionalStandaloneFixture deployment)
    {
        var input = new ExternalDataProtectionTestAuthority("api").Input;
        deployment.SetEnvironment("EVENT_DP_API_RING", input.Ring);
        deployment.SetEnvironment("EVENT_DP_API_ADMISSION", input.Admission);
        deployment.SetEnvironment("EVENT_DP_API_ADMISSION_SHA256", input.AdmissionSha256);
        deployment.SetEnvironment("EVENT_DP_API_MINIMUM_GENERATION", input.MinimumGeneration.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task ExerciseStartupAsync<TEntryPoint>(NativeEmailOptionalStandaloneFixture deployment,
        SeededSubjects subjects, PrivacyErasureIntent retained, int settledOutboxCount, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        var witness = new StartupWitness(deployment.DatabasePath, subjects, retained, pauseReplay: true);
        await using (var host = new RecoveryFactory<TEntryPoint>(deployment, witness))
        {
            Task<HttpClient> startup = Task.Run(() => host.CreateClient(ClientOptions()), CancellationToken.None);
            HttpClient? client = null;
            try
            {
                await Task.WhenAny(witness.ReplayPaused.Task, startup)
                    .WaitAsync(StartupDeadline, cancellationToken);
                if (startup.IsCompleted)
                {
                    client = await startup;
                    throw new InvalidOperationException("The host completed startup without awaiting retained-erasure replay.");
                }
                await witness.ReplayPaused.Task;
                await Assert.That(witness.Starting.Task.IsCompleted).IsFalse();
                await Assert.That(witness.Started.Task.IsCompleted).IsFalse();
                await Assert.That(startup.IsCompleted).IsFalse();
                await AssertRestoredAsync(await ReadStateAsync(deployment.DatabasePath, subjects, retained, cancellationToken));

                witness.ReleaseReplay();
                client = await startup.WaitAsync(StartupDeadline, cancellationToken);
                await AssertStartupAndHttpAsync(host, client, witness, settledOutboxCount, cancellationToken);
                await AssertAuthorityUnchangedAsync(host.Services, retained, cancellationToken);
            }
            finally
            {
                await DrainStartupAsync(startup, client, witness);
            }
        }

        var repeated = new StartupWitness(deployment.DatabasePath, subjects, retained, pauseReplay: false);
        await using var restarted = new RecoveryFactory<TEntryPoint>(deployment, repeated);
        Task<HttpClient> restarting = Task.Run(() => restarted.CreateClient(ClientOptions()), CancellationToken.None);
        HttpClient? restartedClient = null;
        try
        {
            restartedClient = await restarting.WaitAsync(StartupDeadline, cancellationToken);
            await AssertStartupAndHttpAsync(restarted, restartedClient, repeated, settledOutboxCount, cancellationToken);
            await AssertAuthorityUnchangedAsync(restarted.Services, retained, cancellationToken);
        }
        finally
        {
            await DrainStartupAsync(restarting, restartedClient, repeated);
        }
    }

    private static async Task DrainStartupAsync(Task<HttpClient> startup, HttpClient? client, StartupWitness witness)
    {
        witness.ReleaseReplay();
        if (client is not null)
        {
            client.Dispose();
            return;
        }

        witness.StopAvailableHost();
        // WaitAsync does not cancel CreateClient. Keep the deployment authority alive until the original
        // operation terminates, disposing its late client or observing its fault before factory teardown.
        // The native test process must have an outer timeout for an unresponsive framework operation.
        try { (await startup.ConfigureAwait(false)).Dispose(); }
        catch (Exception) { /* The failed acquisition is already propagating; this observes its terminal fault. */ }
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    };

    private static async Task AssertStartupAndHttpAsync<TEntryPoint>(RecoveryFactory<TEntryPoint> host,
        HttpClient client, StartupWitness witness, int settledOutboxCount, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        // StartingAsync runs before any hosted StartAsync, including the HTTP server. These are immutable
        // observations taken at that boundary, rather than reads after CreateClient eventually returns.
        StoredState beforeServer = await witness.BeforeServer.Task.WaitAsync(StartupDeadline, cancellationToken);
        await AssertErasedAsync(beforeServer);
        await Assert.That(beforeServer.OutboxCount).IsEqualTo(settledOutboxCount);
        await Assert.That(witness.StartingWhileReplayHeld).IsFalse();
        await Assert.That(witness.StartedWhileReplayHeld).IsFalse();
        await witness.Started.Task.WaitAsync(StartupDeadline, cancellationToken);
        using HttpResponseMessage alive = await client.GetAsync("/alive", cancellationToken);
        await Assert.That(alive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpClient operational = OperationalEndpointTestTransport.CreateClient(host.Server);
        using HttpResponseMessage health = await operational.GetAsync("/health", cancellationToken);
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertErasedAsync(await witness.ReadAsync(cancellationToken));
    }

    private static async Task AssertAuthorityUnchangedAsync(IServiceProvider services, PrivacyErasureIntent retained,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPrivacyErasureAuthority authority = scope.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>();
        PrivacyErasureAuthorityState state = await authority.GetStateAsync(cancellationToken);
        IReadOnlyList<PrivacyErasureIntent> facts = await authority.ReadAfterAsync(0, 100, cancellationToken);
        await Assert.That(state.HighWaterSequence).IsEqualTo(retained.AuthoritySequence);
        await Assert.That(state.RetainedFloorSequence).IsEqualTo(0L);
        await Assert.That(facts.Count).IsEqualTo(1);
        PrivacyErasureIntent actual = facts.Single();
        await Assert.That(actual.IntentId == retained.IntentId && actual.SubjectId == retained.SubjectId
            && actual.SubjectKind == retained.SubjectKind && actual.ReasonCode == retained.ReasonCode
            && actual.PolicyVersion == retained.PolicyVersion && actual.AuthoritySequence == retained.AuthoritySequence
            && actual.RequestedAtUtc == retained.RequestedAtUtc && actual.RecordedAtUtc == retained.RecordedAtUtc
            && actual.RetentionExpiresAtUtc == retained.RetentionExpiresAtUtc
            && actual.HasSameIdentityFences(retained.IdentityFences.Select(fence => fence.GetFingerprint())))
            .IsTrue();
    }

    private static async Task<SeededSubjects> SeedAsync(ExploreDbContext database, CancellationToken cancellationToken)
    {
        var owner = NewUser();
        var unrelated = NewUser();
        database.AddRange(owner, unrelated);
        await database.SaveChangesAsync(cancellationToken);
        Location home = NewHome(owner.Id);
        Location unrelatedHome = NewHome(unrelated.Id);
        database.AddRange(home, unrelatedHome,
            UserIdentityEmailClaim.Create(owner.Id, owner.Pii!.Email!),
            UserIdentityEmailClaim.Create(unrelated.Id, unrelated.Pii!.Email!));
        await database.SaveChangesAsync(cancellationToken);
        return new(owner.Id, unrelated.Id, home.Id, unrelatedHome.Id);
    }

    private static User NewUser() => new()
    {
        Id = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow, EmailVerified = true,
        Pii = new UserPii { Email = $"startup-{Guid.CreateVersion7():N}@example.test", FirstName = "Synthetic", LastName = "Subject" }
    };

    private static Location NewHome(Guid owner)
    {
        var home = new Location
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            FullName = "Synthetic private venue", Country = "BE", City = "Brussels", ConcurrencyStamp = Guid.CreateVersion7()
        };
        home.ClassifyAsPrivateHome(owner);
        home.SetManualAddress("Synthetic address", "1000");
        return home;
    }

    private static ExploreDbContext OpenPrimary(string path)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = path
        });
        options.UseSnakeCaseNamingConvention();
        var database = new ExploreDbContext(options.Options);
        database.Database.SetCommandTimeout(5);
        return database;
    }

    private static async Task<StoredState> ReadStateAsync(string path, SeededSubjects subjects,
        PrivacyErasureIntent? retained, CancellationToken cancellationToken)
    {
        await using ExploreDbContext database = OpenPrimary(path);
        Location home = await database.Locations.IgnoreQueryFilters().AsNoTracking().Include(value => value.Pii)
            .SingleAsync(value => value.Id == subjects.PrivateHome, cancellationToken);
        Location unrelatedHome = await database.Locations.IgnoreQueryFilters().AsNoTracking().Include(value => value.Pii)
            .SingleAsync(value => value.Id == subjects.UnrelatedHome, cancellationToken);
        PrivacyErasureReplayCheckpoint? checkpoint = await database.PrivacyErasureReplayCheckpoints
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new(
            await database.Users.IgnoreQueryFilters().Where(value => value.Id == subjects.ErasedUser)
                .Select(value => value.IsDeleted).SingleAsync(cancellationToken),
            await database.UserPii.IgnoreQueryFilters().AnyAsync(value => value.UserId == subjects.ErasedUser, cancellationToken),
            await database.UserIdentityEmailClaims.IgnoreQueryFilters().AnyAsync(value => value.UserId == subjects.ErasedUser, cancellationToken),
            home.OwnerUserId == subjects.ErasedUser && home.Pii is not null && home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active,
            home.OwnerUserId is null && home.Pii is null && home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased,
            await database.Users.IgnoreQueryFilters().AnyAsync(value => value.Id == subjects.UnrelatedUser && !value.IsDeleted, cancellationToken)
                && await database.UserPii.IgnoreQueryFilters().AnyAsync(value => value.UserId == subjects.UnrelatedUser, cancellationToken)
                && await database.UserIdentityEmailClaims.IgnoreQueryFilters().AnyAsync(value => value.UserId == subjects.UnrelatedUser, cancellationToken)
                && unrelatedHome.OwnerUserId == subjects.UnrelatedUser && unrelatedHome.Pii is not null
                && unrelatedHome.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active,
            retained is not null && checkpoint is not null && checkpoint.Matches(retained),
            await database.PrivacyErasureReplayCheckpoints.CountAsync(cancellationToken),
            await database.OutboxMessages.CountAsync(value =>
                value.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                || value.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType
                || value.EventType == PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType, cancellationToken));
    }

    private static async Task<CommittedEffects> ReadCommittedEffectsAsync(string path, PrivacyErasureIntent retained,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext database = OpenPrimary(path);
        PrivacyErasureReplayCheckpoint checkpoint = await database.PrivacyErasureReplayCheckpoints
            .AsNoTracking().SingleAsync(cancellationToken);
        PrivacyErasurePolicyCoverage coverage = await database.PrivacyErasurePolicyCoverage
            .AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(checkpoint.Matches(retained) && coverage.IntentId == retained.IntentId
            && coverage.SubjectKind == retained.SubjectKind && coverage.PolicyVersion == retained.PolicyVersion).IsTrue();
        await Assert.That(await database.OutboxMessages.CountAsync(value =>
            value.EventType == PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType, cancellationToken)).IsEqualTo(1);
        await Assert.That(await database.OutboxMessages.CountAsync(value =>
            value.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType, cancellationToken)).IsEqualTo(1);
        await Assert.That(await database.OutboxMessages.CountAsync(value =>
            value.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType, cancellationToken)).IsEqualTo(0);
        Guid[] messages = await database.OutboxMessages.Where(value =>
                value.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                || value.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType
                || value.EventType == PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType)
            .OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync(cancellationToken);
        await Assert.That(messages.Length).IsGreaterThan(0);
        return new(checkpoint.Id, coverage.CoveredAtUtc, messages);
    }

    private static async Task AssertSameEffectsAsync(CommittedEffects expected, CommittedEffects actual)
    {
        await Assert.That(actual.Checkpoint == expected.Checkpoint && actual.CoveredAtUtc == expected.CoveredAtUtc).IsTrue();
        await Assert.That(actual.OutboxIds.SequenceEqual(expected.OutboxIds)).IsTrue();
    }

    private static async Task AssertRestoredAsync(StoredState state)
    {
        await Assert.That(state.SubjectDeleted).IsFalse();
        await Assert.That(state.SubjectPii).IsTrue();
        await Assert.That(state.SubjectEmailClaim).IsTrue();
        await Assert.That(state.SubjectHomePrivate).IsTrue();
        await Assert.That(state.UnrelatedDataIntact).IsTrue();
        await Assert.That(state.CheckpointCount).IsEqualTo(0);
        await Assert.That(state.OutboxCount).IsEqualTo(0);
    }

    private static async Task AssertErasedAsync(StoredState state)
    {
        await Assert.That(state.SubjectDeleted).IsTrue();
        await Assert.That(state.SubjectPii).IsFalse();
        await Assert.That(state.SubjectEmailClaim).IsFalse();
        await Assert.That(state.SubjectHomeErased).IsTrue();
        await Assert.That(state.UnrelatedDataIntact).IsTrue();
        await Assert.That(state.CheckpointMatches).IsTrue();
        await Assert.That(state.CheckpointCount).IsEqualTo(1);
    }

    private static async Task CopyDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
        }.ConnectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5
        }.ConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static void ClearPrimaryPool(string path)
    {
        using var connection = new SqliteConnection(PrimaryDatabaseConfiguration.BuildConnectionString(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = path
        }).ConnectionString);
        SqliteConnection.ClearPool(connection);
    }

    private sealed record SeededSubjects(Guid ErasedUser, Guid UnrelatedUser, Guid PrivateHome, Guid UnrelatedHome);
    private sealed record SessionSnapshot(Guid Id, Guid Tenant, Guid User, byte[] Ciphertext);
    private sealed record SeededSessions(Guid Subject, SessionSnapshot[] Rows);
    private sealed record CommittedEffects(Guid Checkpoint, DateTime CoveredAtUtc, Guid[] OutboxIds);
    private sealed record StoredState(bool SubjectDeleted, bool SubjectPii, bool SubjectEmailClaim, bool SubjectHomePrivate,
        bool SubjectHomeErased, bool UnrelatedDataIntact, bool CheckpointMatches, int CheckpointCount, int OutboxCount);

    private sealed class BorrowedOwnerObservation
    {
        public ExploreDbContext? Expected { get; set; }
        public bool ObservedOriginal { get; private set; }
        public void Observe(ExploreDbContext actual)
        {
            if (Expected is null) return;
            if (!ReferenceEquals(Expected, actual))
                throw new InvalidOperationException("Replay replaced the caller's original Global context.");
            ObservedOriginal = true;
        }
    }

    // The external key resolver serves only generated synthetic session material, never host authentication.
    private sealed class SyntheticSessionSecrets : ISecretResolver
    {
        private readonly string _ring = JsonSerializer.Serialize(new
        {
            keys = new[] { new { kid = Guid.CreateVersion7().ToString("N"),
                k = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), status = "active" } }
        });
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(settingKey == SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing && tenantId is null
                ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, _ring, SecretSourceType.Infisical,
                    SecretScope.Instance, null, DateTimeOffset.UtcNow)) : SecretResolutionResult.Unconfigured);
        }
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId,
            CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);
        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ObservedCheckpointRepository(PrivacyErasureReplayCheckpointRepository native,
        ExploreDbContext database, BorrowedOwnerObservation observation) : IPrivacyErasureReplayCheckpointRepository
    {
        public Task<PrivacyErasureReplayCheckpoint> AppendAsync(PrivacyErasureReplayCheckpoint checkpoint,
            CancellationToken cancellationToken) => native.AppendAsync(checkpoint, cancellationToken);
        public Task<PrivacyErasureReplayCheckpoint?> GetLatestAsync(CancellationToken cancellationToken)
        {
            observation.Observe(database);
            return native.GetLatestAsync(cancellationToken);
        }
    }

    private sealed class ForeignErasureProbe
    {
        public Guid Target { get; set; }
        public Guid Intent { get; set; }
        public Guid InvokedTarget { get; set; }
        public bool Authenticated { get; set; }
        public bool NativeDenied { get; set; }
    }

    private sealed class ForeignErasureFilter(ForeignErasureProbe probe) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (probe.Target == Guid.Empty || context.Controller is not UserController
                || context.ActionDescriptor is not Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action
                || action.MethodInfo.Name != nameof(UserController.DeleteUser))
            {
                await next();
                return;
            }
            var handler = context.HttpContext.RequestServices
                .GetRequiredService<ICommandHandler<DeleteUserCommand, PrivacyErasureStartDto>>();
            var command = new DeleteUserCommand { UserId = probe.Target, IntentId = probe.Intent };
            probe.Authenticated = context.HttpContext.User.Identity?.IsAuthenticated == true;
            probe.InvokedTarget = command.UserId;
            try
            {
                await handler.ExecuteAsync(command, context.HttpContext.RequestAborted);
                context.Result = new StatusCodeResult(500);
            }
            catch (AuthorizationException)
            {
                probe.NativeDenied = true;
                throw;
            }
        }
    }

    private sealed class RecoveryFactory<TEntryPoint>(NativeEmailOptionalStandaloneFixture deployment, StartupWitness witness)
        : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseStaticWebAssets();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailConnectionTester>();
                services.AddSingleton<IEmailConnectionTester>(deployment.Transport);
                services.RemoveAll<IPrivacyErasureReplayCheckpointRepository>();
                services.AddScoped<IPrivacyErasureReplayCheckpointRepository>(provider =>
                {
                    witness.BindLifetime(provider.GetRequiredService<IHostApplicationLifetime>());
                    return new PausedCheckpointRepository(
                        ActivatorUtilities.CreateInstance<PrivacyErasureReplayCheckpointRepository>(provider), witness);
                });
                services.AddSingleton<IHostedService>(provider => new ReplayStartupObserver(
                    witness, provider.GetRequiredService<IHostApplicationLifetime>()));
            });
        }
    }

    private sealed class PausedCheckpointRepository(PrivacyErasureReplayCheckpointRepository native, StartupWitness witness)
        : IPrivacyErasureReplayCheckpointRepository
    {
        public Task<PrivacyErasureReplayCheckpoint> AppendAsync(PrivacyErasureReplayCheckpoint checkpoint,
            CancellationToken cancellationToken) => native.AppendAsync(checkpoint, cancellationToken);

        public async Task<PrivacyErasureReplayCheckpoint?> GetLatestAsync(CancellationToken cancellationToken)
        {
            PrivacyErasureReplayCheckpoint? latest = await native.GetLatestAsync(cancellationToken);
            await witness.PauseReplayAsync(cancellationToken);
            return latest;
        }
    }

    private sealed class CheckpointAppendFault
    {
        public const string FailureCode = "privacy_erasure_saved_checkpoint_fault";
        private int _armed;
        private int _reached;
        public bool ReachedAfterSave => Volatile.Read(ref _reached) != 0;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public void Disarm() => Interlocked.Exchange(ref _armed, 0);

        public void ThrowAfterSavedCheckpoint()
        {
            if (Interlocked.CompareExchange(ref _armed, 0, 1) != 1)
                return;
            Volatile.Write(ref _reached, 1);
            throw new InvalidOperationException(FailureCode);
        }
    }

    private sealed class FaultedCheckpointRepository(PrivacyErasureReplayCheckpointRepository native,
        ExploreDbContext database, CheckpointAppendFault fault) : IPrivacyErasureReplayCheckpointRepository
    {
        public Task<PrivacyErasureReplayCheckpoint?> GetLatestAsync(CancellationToken cancellationToken) =>
            native.GetLatestAsync(cancellationToken);

        public async Task<PrivacyErasureReplayCheckpoint> AppendAsync(PrivacyErasureReplayCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            IDbContextTransaction original = database.Database.CurrentTransaction
                ?? throw new InvalidOperationException("The checkpoint fault requires the original erasure transaction.");
            PrivacyErasureReplayCheckpoint saved = await native.AppendAsync(checkpoint, cancellationToken);
            if (!ReferenceEquals(original, database.Database.CurrentTransaction))
                throw new InvalidOperationException("The checkpoint append replaced its original erasure transaction.");
            fault.ThrowAfterSavedCheckpoint();
            return saved;
        }
    }

    private sealed class StartupWitness(string path, SeededSubjects subjects, PrivacyErasureIntent retained, bool pauseReplay)
    {
        private int _held;
        private IHostApplicationLifetime? _lifetime;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReplayPaused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Starting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<StoredState> BeforeServer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StartingWhileReplayHeld { get; private set; }
        public bool StartedWhileReplayHeld { get; private set; }

        public async Task PauseReplayAsync(CancellationToken cancellationToken)
        {
            if (!pauseReplay || Interlocked.Exchange(ref _held, 1) != 0)
                return;
            ReplayPaused.TrySetResult();
            await _release.Task.WaitAsync(StartupDeadline, cancellationToken);
        }

        public void ReleaseReplay() => _release.TrySetResult();
        public void BindLifetime(IHostApplicationLifetime lifetime) => Interlocked.CompareExchange(ref _lifetime, lifetime, null);
        public void StopAvailableHost() => Volatile.Read(ref _lifetime)?.StopApplication();
        public Task<StoredState> ReadAsync(CancellationToken cancellationToken) => ReadStateAsync(path, subjects, retained, cancellationToken);

        public async Task ObserveStartingAsync(CancellationToken cancellationToken)
        {
            StartingWhileReplayHeld = pauseReplay && !_release.Task.IsCompleted;
            Starting.TrySetResult();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(StartupDeadline);
            StoredState state = await ReadAsync(deadline.Token);
            BeforeServer.TrySetResult(state);
        }

        public void ObserveStarted()
        {
            StartedWhileReplayHeld = pauseReplay && !_release.Task.IsCompleted;
            Started.TrySetResult();
        }
    }

    private sealed class ReplayStartupObserver : IHostedLifecycleService, IDisposable
    {
        private readonly StartupWitness _witness;
        private readonly CancellationTokenRegistration _started;

        public ReplayStartupObserver(StartupWitness witness, IHostApplicationLifetime lifetime)
        {
            _witness = witness;
            _started = lifetime.ApplicationStarted.Register(witness.ObserveStarted);
        }

        public Task StartingAsync(CancellationToken cancellationToken) => _witness.ObserveStartingAsync(cancellationToken);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => _started.Dispose();
    }
}
