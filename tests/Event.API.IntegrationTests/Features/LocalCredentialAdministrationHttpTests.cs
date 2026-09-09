
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Features;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalCredentialAdministrationHttpTests
{
    private const string IdentitiesPath = "/api/instance/local-identities";
    private const string OperationsPath = "/api/instance/local-identity-operations";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;
    public enum Route { List, Create, Reset, Status, Reconcile }
    public enum Caller { InstanceAdministrator, TenantOnly, RevokedPlatformGrant, Anonymous }
    public enum MetadataDefect { Missing, Malformed }
    public enum ForbiddenCreateMember { Actor, InitialPassword, Verification }

    [Test]
    [Arguments(ForbiddenCreateMember.Actor)]
    [Arguments(ForbiddenCreateMember.InitialPassword)]
    [Arguments(ForbiddenCreateMember.Verification)]
    public async Task CreateRejectsCallerSuppliedAuthorityAndCredentialFields(ForbiddenCreateMember member)
    {
        await using var fixture = await Fixture.CreateAsync();
        JsonObject body = JsonSerializer.SerializeToNode(CreateBody(Guid.CreateVersion7()))!.AsObject();
        switch (member)
        {
            case ForbiddenCreateMember.Actor:
                body.Add("initiatingApplicationUserId", JsonValue.Create(Guid.CreateVersion7()));
                break;
            case ForbiddenCreateMember.InitialPassword:
                body.Add("initialPassword", JsonValue.Create(NewPassword()));
                break;
            case ForbiddenCreateMember.Verification:
                body.Add("emailVerified", JsonValue.Create(true));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member));
        }
        string before = await fixture.SnapshotAsync();

        using HttpResponseMessage response = await fixture.SendAsAdministratorAsync(HttpMethod.Post, IdentitiesPath, body);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await ReadAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.BadRequest);
        AssertPrivate(response);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(Route.List, Caller.TenantOnly)]
    [Arguments(Route.Create, Caller.TenantOnly)]
    [Arguments(Route.Reset, Caller.TenantOnly)]
    [Arguments(Route.Status, Caller.TenantOnly)]
    [Arguments(Route.Reconcile, Caller.TenantOnly)]
    [Arguments(Route.List, Caller.RevokedPlatformGrant)]
    [Arguments(Route.Create, Caller.RevokedPlatformGrant)]
    [Arguments(Route.Reset, Caller.RevokedPlatformGrant)]
    [Arguments(Route.Status, Caller.RevokedPlatformGrant)]
    [Arguments(Route.Reconcile, Caller.RevokedPlatformGrant)]
    [Arguments(Route.List, Caller.Anonymous)]
    [Arguments(Route.Create, Caller.Anonymous)]
    [Arguments(Route.Reset, Caller.Anonymous)]
    [Arguments(Route.Status, Caller.Anonymous)]
    [Arguments(Route.Reconcile, Caller.Anonymous)]
    public async Task EveryRouteRequiresCurrentPersistedInstanceAuthority(Route route, Caller caller)
    {
        await using var fixture = await Fixture.CreateAsync();
        LocalCredentialCreateResult pending = await fixture.CreatePendingAsync();
        string? token = fixture.Bearer;
        if (caller == Caller.Anonymous) token = null;
        else
        {
            await using ExploreDbContext database = fixture.Factory.CreateDatabase();
            await database.PlatformUserRoles.Where(grant => grant.UserId == fixture.AdministratorId)
                .ExecuteDeleteAsync(CancellationToken);
            if (caller == Caller.TenantOnly)
            {
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.AdministratorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = member.TenantId,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = member,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                await database.SaveChangesAsync(CancellationToken);
            }
            using HttpResponseMessage stillAuthenticated = await fixture.SendAsync(HttpMethod.Get, "/api/user", token: token);
            await Assert.That(stillAuthenticated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        string before = await fixture.SnapshotAsync();
        Guid operationId = pending.Receipt!.OperationId;
        (HttpMethod method, string path, object? body) = route switch
        {
            Route.List => (HttpMethod.Get, IdentitiesPath, null),
            Route.Create => (HttpMethod.Post, IdentitiesPath, CreateBody(Guid.CreateVersion7())),
            Route.Reset => (HttpMethod.Post, ResetPath(fixture.AdministratorId),
                await fixture.ResetBodyAsync(fixture.AdministratorId, Guid.CreateVersion7())),
            Route.Status => (HttpMethod.Get, StatusPath(operationId), null),
            Route.Reconcile => (HttpMethod.Post, StatusPath(operationId) + "/reconcile", null),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        using HttpResponseMessage denied = await fixture.SendAsync(method, path, body, token);

        HttpStatusCode expected = caller == Caller.Anonymous && method == HttpMethod.Post
            ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        await Assert.That(denied.StatusCode).IsEqualTo(expected);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AdministratorCanDiscoverIssueReconcileAndResetWithoutKeepingPriorSessionAuthority(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology: topology);
        using HttpResponseMessage overview = await fixture.SendAsAdministratorAsync(
            HttpMethod.Get, "/api/admin/control-plane/overview");
        await Assert.That(overview.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string collectionHref = (await ReadAsync(overview)).GetProperty("_links")
            .GetProperty("local-identities").GetProperty("href").GetString()!;
        var collectionUri = new Uri(fixture.Client.BaseAddress!, collectionHref);
        await Assert.That(collectionUri.AbsolutePath).IsEqualTo(IdentitiesPath);
        Guid operationId = Guid.CreateVersion7();
        object body = CreateBody(operationId);
        using HttpResponseMessage created = await fixture.SendAsAdministratorAsync(HttpMethod.Post, IdentitiesPath, body);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        JsonElement issue = await ReadAsync(created);
        Guid subjectId = Receipt(issue.GetProperty("operation")).GetProperty("localSubjectId").GetGuid();
        string temporaryPassword = issue.GetProperty("temporaryPassword").GetString()!;
        string email = await fixture.EmailAsync(subjectId);
        await Assert.That(Receipt(issue.GetProperty("operation")).GetProperty("operationId").GetGuid()).IsEqualTo(operationId);
        AssertPrivate(created);
        if (topology == IdentityDatabaseTopology.External)
        {
            await using ExploreDbContext application = fixture.Factory.CreateDatabase();
            await Assert.That(await application.LocalIdentityUsers.AnyAsync(CancellationToken)).IsFalse();
            // Deliberately invalid same-subject data in the non-authoritative store must not govern API reads or reset.
            application.LocalIdentityUsers.Add(new LocalIdentityUser
            {
                Id = subjectId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "Wrong store",
                LastName = "Decoy",
                EmailConfirmed = false,
                CreatedAt = DateTime.UtcNow
            });
            application.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
            {
                UserId = subjectId,
                LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
                Name = LocalCredentialStateMetadata.TokenName,
                Value = "{"
            });
            await application.SaveChangesAsync(CancellationToken);
        }

        using HttpResponseMessage listed = await fixture.SendAsAdministratorAsync(HttpMethod.Get, collectionUri.PathAndQuery);
        await Assert.That(listed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement list = await ReadAsync(listed);
        await Assert.That(list.GetProperty("_links").TryGetProperty("create-local-identity", out _)).IsTrue();
        JsonElement target = list.GetProperty("_embedded").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("localSubjectId").GetGuid() == subjectId);
        await Assert.That(target.GetProperty("_links").TryGetProperty("issue-temporary-credential", out _)).IsTrue();
        await Assert.That(target.GetProperty("firstName").GetString()).IsEqualTo("Created");
        await AssertSecretAbsentAsync(list, temporaryPassword);
        using (HttpResponseMessage status = await fixture.SendAsAdministratorAsync(HttpMethod.Get, StatusPath(operationId)))
        {
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(Receipt(await ReadAsync(status)).GetProperty("localSubjectId").GetGuid()).IsEqualTo(subjectId);
        }
        using HttpResponseMessage reconciled = await fixture.SendAsAdministratorAsync(
            HttpMethod.Post, StatusPath(operationId) + "/reconcile");
        await Assert.That(reconciled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertSecretAbsentAsync(await ReadAsync(reconciled), temporaryPassword);

        string readyPassword = NewPassword();
        await fixture.ReplaceThroughHttpAsync(email, temporaryPassword, readyPassword);
        string oldBearer = await fixture.LoginAsync(new LocalAuthRequestDto(Identifier: email, Password: readyPassword));
        using (HttpResponseMessage current = await fixture.SendAsync(HttpMethod.Get, "/api/user", token: oldBearer))
            await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid resetId = Guid.CreateVersion7();
        using HttpResponseMessage reset = await fixture.SendAsAdministratorAsync(HttpMethod.Post, ResetPath(subjectId),
            await fixture.ResetBodyAsync(subjectId, resetId));
        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement resetIssue = await ReadAsync(reset);
        using (HttpResponseMessage revoked = await fixture.SendAsync(HttpMethod.Get, "/api/user", token: oldBearer))
            await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        string finalPassword = NewPassword();
        await fixture.ReplaceThroughHttpAsync(email, resetIssue.GetProperty("temporaryPassword").GetString()!, finalPassword);
        string freshBearer = await fixture.LoginAsync(new LocalAuthRequestDto(Identifier: email, Password: finalPassword));
        using HttpResponseMessage restored = await fixture.SendAsync(HttpMethod.Get, "/api/user", token: freshBearer);
        await Assert.That(restored.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadAsync(restored)).GetProperty("id").GetGuid()).IsEqualTo(subjectId);
        if (topology == IdentityDatabaseTopology.External)
        {
            await using ExploreDbContext application = fixture.Factory.CreateDatabase();
            LocalIdentityUser decoy = await application.LocalIdentityUsers.SingleAsync(CancellationToken);
            await Assert.That(decoy.FirstName).IsEqualTo("Wrong store");
            await Assert.That(decoy.PasswordHash).IsNull();
            await Assert.That(await application.Set<LocalIdentityCredentialOperation>().AnyAsync(CancellationToken)).IsFalse();
            await Assert.That(await application.Set<IdentityUserToken<Guid>>().Select(token => token.Value).SingleAsync(CancellationToken))
                .IsEqualTo("{");
        }
    }

    [Test]
    [Arguments(Caller.InstanceAdministrator)]
    [Arguments(Caller.TenantOnly)]
    public async Task EmptyReconcileOperationIsValidatedAfterCurrentAuthority(Caller caller)
    {
        await using var fixture = await Fixture.CreateAsync();
        // The first case retains its platform grant; the second has only tenant authority.
        if (caller == Caller.TenantOnly)
        {
            await using ExploreDbContext database = fixture.Factory.CreateDatabase();
            await database.PlatformUserRoles.Where(grant => grant.UserId == fixture.AdministratorId).ExecuteDeleteAsync(CancellationToken);
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.AdministratorId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken);
        }
        string before = await fixture.SnapshotAsync();
        using HttpResponseMessage response = await fixture.SendAsAdministratorAsync(HttpMethod.Post, StatusPath(Guid.Empty) + "/reconcile");
        HttpStatusCode expected = caller == Caller.TenantOnly ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest;
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That((await ReadAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)expected);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(Route.Create)]
    [Arguments(Route.Reset)]
    public async Task PlaintextIsOneTimeAndNeverStoredByGenericIdempotency(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid operationId = Guid.CreateVersion7();
        string path = route == Route.Create ? IdentitiesPath : ResetPath(fixture.AdministratorId);
        object body = route == Route.Create ? CreateBody(operationId)
            : await fixture.ResetBodyAsync(fixture.AdministratorId, operationId);
        // Reset another Ready account so the administrator retains authority to observe replay.
        if (route == Route.Reset)
        {
            LocalAuthRequestDto target = await fixture.Factory.SeedLocalUserAsync(emailConfirmed: true);
            Guid targetId = await fixture.SubjectIdAsync(target.Identifier);
            path = ResetPath(targetId);
            body = await fixture.ResetBodyAsync(targetId, operationId);
        }
        string key = Guid.CreateVersion7().ToString("N");
        using HttpResponseMessage first = await fixture.SendAsAdministratorAsync(HttpMethod.Post, path, body, key);
        await Assert.That(first.StatusCode).IsEqualTo(route == Route.Create ? HttpStatusCode.Created : HttpStatusCode.OK);
        JsonElement issued = await ReadAsync(first);
        string password = issued.GetProperty("temporaryPassword").GetString()!;
        await Assert.That(string.IsNullOrWhiteSpace(password)).IsFalse();
        await Assert.That(fixture.Logs.Messages.Count).IsGreaterThan(0);
        await Assert.That(fixture.Logs.Messages.Any(message => message.Contains(password, StringComparison.Ordinal))).IsFalse();
        LocalCredentialOperationStatus issuedStatus = issued.GetProperty("operation")
            .Deserialize<LocalCredentialOperationStatus>(JsonOptions)!;
        LocalCredentialIssueDto diagnosticValue = LocalCredentialIssueDto.Issued(issuedStatus, password);
        await Assert.That(diagnosticValue.ToString().Contains(password, StringComparison.Ordinal)).IsFalse();
        AssertPrivate(first);
        string beforeReplay = await fixture.SnapshotAsync();
        foreach (string replayKey in new[] { key, Guid.CreateVersion7().ToString("N") })
        {
            using HttpResponseMessage replay = await fixture.SendAsAdministratorAsync(HttpMethod.Post, path, body, replayKey);
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
            JsonElement replayed = await ReadAsync(replay);
            await Assert.That(replayed.GetProperty("outcome").Deserialize<LocalCredentialIssueOutcome>(JsonOptions))
                .IsEqualTo(LocalCredentialIssueOutcome.Replayed);
            await AssertSecretAbsentAsync(replayed, password);
            await fixture.AssertUnchangedAsync(beforeReplay);
            await using ExploreDbContext database = fixture.Factory.CreateDatabase();
            await Assert.That(await database.Set<IdempotencyRecord>().AnyAsync(record => record.Key == replayKey, CancellationToken)).IsFalse();
        }
        using HttpResponseMessage status = await fixture.SendAsAdministratorAsync(HttpMethod.Get, StatusPath(operationId));
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertSecretAbsentAsync(await ReadAsync(status), password);
        await Assert.That(fixture.Logs.Messages.Any(message => message.Contains(password, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task NonLocalAccountCannotReceiveLocalReset()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid externalId = Guid.CreateVersion7();
        await using (ExploreDbContext database = fixture.Factory.CreateDatabase())
        {
            var user = new User
            {
                Id = externalId,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = "external@example.test", FirstName = "External", LastName = "Owner" }
            };
            database.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = Guid.CreateVersion7().ToString("D"),
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken);
        }
        string before = await fixture.SnapshotAsync();
        using HttpResponseMessage denied = await fixture.SendAsAdministratorAsync(HttpMethod.Post, ResetPath(externalId), new
        {
            operationId = Guid.CreateVersion7(),
            expectedCurrentOperationId = Guid.CreateVersion7(),
            expectedCurrentOperationConcurrencyStamp = Guid.CreateVersion7(),
            reason = "Exact provider boundary"
        });
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    public async Task FailedApplicationBindingLeavesRecoverablePendingOperationWithoutGetSideEffects()
    {
        var fault = new BindingWriteFault();
        await using var fixture = await Fixture.CreateAsync(fault);
        Guid operationId = Guid.CreateVersion7();
        fault.Arm();
        using HttpResponseMessage failed = await fixture.SendAsAdministratorAsync(HttpMethod.Post, IdentitiesPath, CreateBody(operationId));
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That((await ReadAsync(failed)).TryGetProperty("temporaryPassword", out _)).IsFalse();
        await Assert.That(fault.Observed).IsTrue();
        LocalIdentityCredentialOperation operation = await fixture.OperationAsync(operationId);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        await fixture.AssertBindingAsync(operation, expected: false);
        string beforeGet = await fixture.SnapshotAsync();
        using HttpResponseMessage status = await fixture.SendAsAdministratorAsync(HttpMethod.Get, StatusPath(operationId));
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement pending = await ReadAsync(status);
        await Assert.That(pending.GetProperty("_links").TryGetProperty("reconcile", out _)).IsTrue();
        await fixture.AssertUnchangedAsync(beforeGet);
        using HttpResponseMessage reconciled = await fixture.SendAsAdministratorAsync(HttpMethod.Post, StatusPath(operationId) + "/reconcile");
        await Assert.That(reconciled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadAsync(reconciled)).TryGetProperty("temporaryPassword", out _)).IsFalse();
        operation = await fixture.OperationAsync(operationId);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ChangeRequired);
        await fixture.AssertBindingAsync(operation, expected: true);
    }

    [Test]
    public async Task ReconcileCannotAdoptConflictingPartialBinding()
    {
        await using var fixture = await Fixture.CreateAsync();
        LocalCredentialCreateResult pending = await fixture.CreatePendingAsync();
        await using (ExploreDbContext database = fixture.Factory.CreateDatabase())
        {
            database.Users.Add(new User
            {
                Id = pending.Receipt!.ApplicationUserId,
                EmailVerified = false,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = "conflict@example.test", FirstName = "Different", LastName = "Owner" }
            });
            await database.SaveChangesAsync(CancellationToken);
        }
        string before = await fixture.SnapshotAsync();
        using HttpResponseMessage conflict = await fixture.SendAsAdministratorAsync(HttpMethod.Post,
            StatusPath(pending.Receipt!.OperationId) + "/reconcile");
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    public async Task ReconcileIdempotencyReplayCannotRestoreRevokedAdministratorAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        LocalCredentialCreateResult pending = await fixture.CreatePendingAsync();
        string path = StatusPath(pending.Receipt!.OperationId) + "/reconcile";
        string key = Guid.CreateVersion7().ToString("N");
        using (HttpResponseMessage first = await fixture.SendAsAdministratorAsync(HttpMethod.Post, path, key: key))
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string committed = await fixture.SnapshotAsync();
        using (HttpResponseMessage legitimateReplay = await fixture.SendAsAdministratorAsync(HttpMethod.Post, path, key: key))
            await Assert.That(legitimateReplay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await fixture.AssertUnchangedAsync(committed);

        await using (ExploreDbContext database = fixture.Factory.CreateDatabase())
            await database.PlatformUserRoles.Where(grant => grant.UserId == fixture.AdministratorId)
                .ExecuteDeleteAsync(CancellationToken);
        using (HttpResponseMessage stillAuthenticated = await fixture.SendAsync(HttpMethod.Get, "/api/user", token: fixture.Bearer))
            await Assert.That(stillAuthenticated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string beforeDeniedReplay = await fixture.SnapshotAsync();

        using HttpResponseMessage denied = await fixture.SendAsAdministratorAsync(HttpMethod.Post, path, key: key);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        AssertPrivate(denied);
        JsonElement problem = await ReadAsync(denied);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.Forbidden);
        await Assert.That(problem.TryGetProperty("receipt", out _)).IsFalse();
        await Assert.That(problem.TryGetProperty("operation", out _)).IsFalse();
        await fixture.AssertUnchangedAsync(beforeDeniedReplay);
    }

    [Test]
    public async Task RepeatedDiscoveryAndStatusReadsCannotActivatePendingCredentials()
    {
        await using var fixture = await Fixture.CreateAsync();
        LocalCredentialCreateResult pending = await fixture.CreatePendingAsync();
        string before = await fixture.SnapshotAsync();
        for (int read = 0; read < 2; read++)
        {
            using HttpResponseMessage list = await fixture.SendAsAdministratorAsync(HttpMethod.Get, IdentitiesPath);
            using HttpResponseMessage status = await fixture.SendAsAdministratorAsync(HttpMethod.Get, StatusPath(pending.Receipt!.OperationId));
            await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertSecretAbsentAsync(await ReadAsync(list), pending.TemporaryPassword!);
            await AssertSecretAbsentAsync(await ReadAsync(status), pending.TemporaryPassword!);
            await fixture.AssertUnchangedAsync(before);
        }
        using HttpResponseMessage login = await fixture.Client.PostAsJsonAsync("/api/auth/local/login",
            new LocalAuthRequestDto(Identifier: await fixture.EmailAsync(pending.Receipt!.LocalSubjectId), Password: pending.TemporaryPassword!), CancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        JsonElement denied = await ReadAsync(login);
        await Assert.That(denied.GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.Unauthorized);
        await Assert.That(denied.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(denied.TryGetProperty("replacementChallenge", out _)).IsFalse();
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(0, 20)]
    [Arguments(1, 0)]
    [Arguments(1, 101)]
    [Arguments(int.MaxValue, 100)]
    public async Task InvalidOrOverflowingPageCannotReadUnboundedIdentityData(int pageNumber, int pageSize)
    {
        await using var fixture = await Fixture.CreateAsync();
        string before = await fixture.SnapshotAsync();
        using HttpResponseMessage response = await fixture.SendAsAdministratorAsync(HttpMethod.Get,
            $"{IdentitiesPath}?pageNumber={pageNumber}&pageSize={pageSize}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(MetadataDefect.Missing)]
    [Arguments(MetadataDefect.Malformed)]
    public async Task MissingOrMalformedLifecycleMetadataNeverGetsResetAffordance(MetadataDefect defect)
    {
        await using var fixture = await Fixture.CreateAsync();
        LocalAuthRequestDto target = await fixture.Factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid subjectId = await fixture.SubjectIdAsync(target.Identifier);
        await using (AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(subjectId.ToString("D")))!;
            IdentityResult result = defect == MetadataDefect.Malformed
                ? await manager.SetAuthenticationTokenAsync(user, LocalCredentialStateMetadata.TokenLoginProvider,
                    LocalCredentialStateMetadata.TokenName, "{")
                : await manager.RemoveAuthenticationTokenAsync(user, LocalCredentialStateMetadata.TokenLoginProvider,
                    LocalCredentialStateMetadata.TokenName);
            await Assert.That(result.Succeeded).IsTrue();
        }
        string before = await fixture.SnapshotAsync();
        using HttpResponseMessage response = await fixture.SendAsAdministratorAsync(HttpMethod.Get, IdentitiesPath);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement list = await ReadAsync(response);
        JsonElement item = list.GetProperty("_embedded").GetProperty("items").EnumerateArray()
            .Single(candidate => candidate.GetProperty("localSubjectId").GetGuid() == subjectId);
        await Assert.That(!item.TryGetProperty("_links", out JsonElement links)
            || !links.TryGetProperty("issue-temporary-credential", out _)).IsTrue();
        await Assert.That(!item.TryGetProperty("credentialState", out JsonElement state)
            || state.ValueKind == JsonValueKind.Null).IsTrue();
        await Assert.That(!item.TryGetProperty("currentOperationId", out JsonElement operation)
            || operation.ValueKind == JsonValueKind.Null).IsTrue();
        await fixture.AssertUnchangedAsync(before);
    }

    private static string ResetPath(Guid subjectId) => $"{IdentitiesPath}/{subjectId:D}/temporary-credential";
    private static string StatusPath(Guid operationId) => $"{OperationsPath}/{operationId:D}";
    private static object CreateBody(Guid operationId) => new
    {
        operationId,
        email = $"created-{operationId:N}@example.test",
        firstName = "Created",
        lastName = "Owner"
    };
    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
    private static JsonElement Receipt(JsonElement status) => status.GetProperty("receipt");
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken),
            cancellationToken: CancellationToken);
        return document.RootElement.Clone();
    }
    private static async Task AssertSecretAbsentAsync(JsonElement body, string password)
    {
        await Assert.That(body.GetRawText().Contains(password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(body.TryGetProperty("temporaryPassword", out _)).IsFalse();
    }
    private static void AssertPrivate(HttpResponseMessage response)
    {
        if (response.Headers.CacheControl is not { Private: true, NoStore: true })
            throw new InvalidOperationException("Credential responses must be private and non-storable.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal LocalAdmissionWebApplicationFactory Factory { get; private init; } = null!;
        internal HttpClient Client { get; private init; } = null!;
        internal Guid AdministratorId { get; private set; }
        internal string Bearer { get; private set; } = null!;
        internal CapturingLoggerProvider Logs { get; private init; } = null!;

        internal static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null,
            IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated)
        {
            var logs = new CapturingLoggerProvider();
            var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
                persistenceInterceptor: interceptor, logCapture: logs, identityTopology: topology);
            var fixture = new Fixture
            {
                Factory = factory,
                Logs = logs,
                Client = factory.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false })
            };
            try
            {
                LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: true);
                fixture.AdministratorId = await fixture.SubjectIdAsync(login.Identifier);
                await using (ExploreDbContext database = factory.CreateDatabase())
                {
                    Role role = await database.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin", CancellationToken);
                    database.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = fixture.AdministratorId,
                        User = null!,
                        RoleId = role.Id,
                        Role = role,
                        GrantedAt = DateTime.UtcNow
                    });
                    await database.SaveChangesAsync(CancellationToken);
                }
                // These native signed role claims intentionally survive deletion of the platform grant.
                await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                {
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<LocalIdentityRole>>();
                    await Assert.That((await roles.CreateAsync(new LocalIdentityRole("platform.admin"))).Succeeded).IsTrue();
                    var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
                    LocalIdentityUser user = (await manager.FindByIdAsync(fixture.AdministratorId.ToString("D")))!;
                    await Assert.That((await manager.AddToRoleAsync(user, "platform.admin")).Succeeded).IsTrue();
                }
                fixture.Bearer = await fixture.LoginAsync(login);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal Task<HttpResponseMessage> SendAsAdministratorAsync(HttpMethod method, string path, object? body = null, string? key = null) =>
            SendAsync(method, path, body, Bearer, key);
        internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? token = null, string? key = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            return await Client.SendAsync(request, CancellationToken);
        }
        internal async Task<string> LoginAsync(LocalAuthRequestDto login)
        {
            using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/auth/local/login", login, CancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            JsonElement body = await ReadAsync(response);
            await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
            return body.GetProperty("token").GetString()!;
        }
        internal async Task ReplaceThroughHttpAsync(string email, string temporaryPassword, string newPassword)
        {
            using HttpResponseMessage login = await Client.PostAsJsonAsync("/api/auth/local/login",
                new LocalAuthRequestDto(Identifier: email, Password: temporaryPassword), CancellationToken);
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            string challenge = (await ReadAsync(login)).GetProperty("replacementChallenge").GetProperty("token").GetString()!;
            using HttpResponseMessage replaced = await SendAsync(HttpMethod.Post, "/api/auth/local/credential-replacement",
                new { newPassword }, challenge);
            await Assert.That(replaced.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        internal async Task<Guid> SubjectIdAsync(string email)
        {
            await using DbContext database = Factory.CreateIdentityDatabase();
            return await database.Set<LocalIdentityUser>().Where(user => user.Email == email).Select(user => user.Id).SingleAsync(CancellationToken);
        }
        internal async Task<string> EmailAsync(Guid subjectId)
        {
            await using DbContext database = Factory.CreateIdentityDatabase();
            return (await database.Set<LocalIdentityUser>().Where(user => user.Id == subjectId).Select(user => user.Email).SingleAsync(CancellationToken))!;
        }
        internal async Task<object> ResetBodyAsync(Guid subjectId, Guid operationId)
        {
            await using DbContext database = Factory.CreateIdentityDatabase();
            string value = (await database.Set<IdentityUserToken<Guid>>().Where(token => token.UserId == subjectId
                && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName).Select(token => token.Value).SingleAsync(CancellationToken))!;
            var state = JsonSerializer.Deserialize<LocalCredentialStateMetadata>(value, JsonOptions)!;
            LocalIdentityCredentialOperation current = await OperationAsync(state.OperationId);
            return new
            {
                operationId,
                expectedCurrentOperationId = current.Id,
                expectedCurrentOperationConcurrencyStamp = current.ConcurrencyStamp,
                reason = "Supervised HTTP reset"
            };
        }
        internal async Task<LocalCredentialCreateResult> CreatePendingAsync()
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            Guid operationId = Guid.CreateVersion7();
            LocalCredentialCreateResult result = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .CreatePendingAsync(new LocalCredentialCreateRequest(operationId: operationId,
                    initiatingApplicationUserId: AdministratorId, email: $"pending-{operationId:N}@example.test",
                    firstName: "Pending", lastName: "Owner"), CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            return result;
        }
        internal async Task<LocalIdentityCredentialOperation> OperationAsync(Guid operationId)
        {
            await using DbContext database = Factory.CreateIdentityDatabase();
            return await database.Set<LocalIdentityCredentialOperation>().AsNoTracking().SingleAsync(row => row.Id == operationId, CancellationToken);
        }
        internal async Task AssertBindingAsync(LocalIdentityCredentialOperation operation, bool expected)
        {
            await using ExploreDbContext database = Factory.CreateDatabase();
            await Assert.That(await database.Users.AnyAsync(user => user.Id == operation.ApplicationUserId, CancellationToken)).IsEqualTo(expected);
            await Assert.That(await database.Actors.AnyAsync(actor => actor.Id == operation.PersonalActorId
                && actor.UserId == operation.ApplicationUserId, CancellationToken)).IsEqualTo(expected);
            await Assert.That(await database.UserExternalLogins.AnyAsync(login => login.Id == operation.ExternalLoginId
                && login.UserId == operation.ApplicationUserId && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local,
                CancellationToken)).IsEqualTo(expected);
        }
        internal async Task<string> SnapshotAsync()
        {
            await using ExploreDbContext database = Factory.CreateDatabase();
            await using DbContext identity = Factory.CreateIdentityDatabase();
            return JsonSerializer.Serialize(new
            {
                Credentials = await identity.Set<LocalIdentityUser>().AsNoTracking().OrderBy(user => user.Id)
                    .Select(user => new { user.Id, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp }).ToArrayAsync(CancellationToken),
                Operations = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(CancellationToken),
                States = await identity.Set<IdentityUserToken<Guid>>().AsNoTracking().OrderBy(row => row.UserId)
                    .ThenBy(row => row.LoginProvider).ThenBy(row => row.Name).ToArrayAsync(CancellationToken),
                Users = await database.Users.AsNoTracking().OrderBy(user => user.Id)
                    .Select(user => new { user.Id, user.EmailVerified, user.IsDeleted }).ToArrayAsync(CancellationToken),
                Actors = await database.Actors.AsNoTracking().OrderBy(actor => actor.Id)
                    .Select(actor => new { actor.Id, actor.UserId, actor.ActorTypeId }).ToArrayAsync(CancellationToken),
                Bindings = await database.UserExternalLogins.AsNoTracking().OrderBy(login => login.Id)
                    .Select(login => new { login.Id, login.UserId, login.AuthenticationProviderId, login.ProviderKey }).ToArrayAsync(CancellationToken)
            });
        }
        internal async Task AssertUnchangedAsync(string before) =>
            await Assert.That(string.Equals(await SnapshotAsync(), before, StringComparison.Ordinal)).IsTrue();
        public async ValueTask DisposeAsync() { Client.Dispose(); await Factory.DisposeAsync(); }
    }

    private sealed class InjectedBindingFailure : Exception;
    private sealed class BindingWriteFault : DbCommandInterceptor
    {
        private bool _armed;
        internal bool Observed { get; private set; }
        internal void Arm() => _armed = true;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (_armed && eventData.CommandSource == CommandSource.SaveChanges
                && eventData.Context!.ChangeTracker.Entries<User>().Any(entry => entry.State == EntityState.Added))
            {
                _armed = false;
                Observed = true;
                throw new InjectedBindingFailure();
            }
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
