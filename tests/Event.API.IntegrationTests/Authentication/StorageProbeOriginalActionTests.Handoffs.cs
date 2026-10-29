using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("binding-rewired")]
    [Arguments("session-revoked")]
    [Arguments("administrator-revoked")]
    [Arguments("provider-replaced")]
    [Arguments("endpoint-replaced")]
    [Arguments("secret-binding-replaced")]
    [Arguments("credential-replaced")]
    [Arguments("credential-revision-replaced")]
    [Arguments("credential-expired")]
    [Arguments("principal-replaced")]
    public async Task RealSourcesSurvivePreparationAndAreRecheckedBeforeTheActualPut(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grant;
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            grant = await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterVersioning = async () =>
        {
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change == "credential-replaced") remote.Credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            if (change == "credential-revision-replaced") remote.Revision = Guid.CreateVersion7().ToString("N");
            if (change == "credential-expired")
            {
                var lifetime = selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }
            await using var independent = CreateProbeDatabase(fixture.Native);
            if (change == "binding-rewired") await RewireProbeBindingAsync(fixture);
            if (change == "session-revoked") await RevokeProbeSessionAsync(fixture);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync();
            if (change is "provider-replaced" or "endpoint-replaced")
            {
                string key = change == "provider-replaced" ? GovernanceSettingKeys.Storage.Provider : GovernanceSettingKeys.Storage.Endpoint;
                await independent.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value,
                        change == "provider-replaced" ? "\"local\"" : "\"https://foreign.invalid\""));
            }
            if (change == "secret-binding-replaced")
            {
                independent.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                    SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, SecretScope.Instance, null, "PROBE_REPLACEMENT"));
                await independent.SaveChangesAsync();
            }
        };
        var query = new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId };
        var execution = ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
            .QueryAsync(query, CancellationToken.None);
        if (change == "unchanged")
        {
            await Assert.That((await execution).IsAvailable).IsTrue();
            await Assert.That(remote.Puts).IsEqualTo(1);
            await Assert.That(remote.Deletes).IsEqualTo(1);
            await Assert.That(remote.Objects.Single().Version).IsEqualTo("foreign-version");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.Services
                .GetRequiredService<IStorageProbeOriginalAction>().ExecuteAsync(query, CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
            await Assert.That(remote.Puts).IsEqualTo(0);
            await Assert.That(remote.Deletes).IsEqualTo(0);
        }
        await Assert.That(remote.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("expired-before-cleanup")]
    [Arguments("binding-before-cleanup")]
    [Arguments("credential-before-cleanup")]
    [Arguments("binding-rewired-before-return")]
    [Arguments("session-revoked-before-return")]
    [Arguments("foreign-collision")]
    [Arguments("ambiguous-put")]
    [Arguments("cleanup-failed")]
    [Arguments("unversioned")]
    [Arguments("missing-version")]
    public async Task ActualProbeOnlyDeletesItsAcknowledgedVersionUnderOriginalAuthority(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote { Failure = change };
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterPut = async () =>
        {
            if (change == "expired-before-cleanup")
            {
                var lifetime = selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }
            if (change == "credential-before-cleanup")
                remote.Credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            if (change == "binding-before-cleanup")
            {
                await using var independent = CreateProbeDatabase(fixture.Native);
                await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"foreign-bucket\""));
            }
        };
        remote.AfterDelete = async () =>
        {
            if (change == "binding-rewired-before-return") await RewireProbeBindingAsync(fixture);
            if (change == "session-revoked-before-return") await RevokeProbeSessionAsync(fixture);
        };
        var execution = ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
            .QueryAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None);
        if (change is "expired-before-cleanup" or "binding-before-cleanup" or "credential-before-cleanup"
            or "binding-rewired-before-return" or "session-revoked-before-return")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else
        {
            var result = await execution;
            await Assert.That(result.IsAvailable).IsEqualTo(change == "unchanged");
            if (change != "unchanged")
            {
                await Assert.That(result.FailureCode).IsEqualTo(change switch
                {
                    "unversioned" => "probe_exact_cleanup_unsupported",
                    "missing-version" => "probe_cleanup_ownership_unconfirmed",
                    "cleanup-failed" => "probe_cleanup_failed",
                    _ => "probe_write_unconfirmed"
                });
                await Assert.That(result.Message!.Contains(remote.Credential, StringComparison.Ordinal)).IsFalse();
            }
        }
        bool deleted = change is "unchanged" or "binding-rewired-before-return" or "session-revoked-before-return";
        await Assert.That(remote.Deletes).IsEqualTo(deleted || change == "cleanup-failed" ? 1 : 0);
        await Assert.That(remote.Objects.Any(row => row.Version == "probe-version"))
            .IsEqualTo(!deleted && change is not ("unversioned" or "foreign-collision"));
        await Assert.That(remote.Objects.Any(row => row.Version == "foreign-version")).IsEqualTo(change != "unversioned");
    }

    private static async Task RewireProbeBindingAsync(LocalIdentityLifecycleHttpFixture fixture, CancellationToken token = default)
    {
        await using var independent = CreateProbeDatabase(fixture.Native);
        string subject = Guid.CreateVersion7().ToString("D");
        await Assert.That(await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, subject), token)).IsEqualTo(1);
    }

    private static async Task RevokeProbeSessionAsync(LocalIdentityLifecycleHttpFixture fixture, CancellationToken token = default)
    {
        await using var observer = CreateProbeDatabase(fixture.Native);
        var original = await observer.UserExternalLogins.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Binding.ExternalLoginId, token);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = (await manager.FindByIdAsync(fixture.Binding.LocalSubjectId.ToString("D")))!;
        string? originalStamp = user.SecurityStamp;
        await Assert.That((await manager.UpdateSecurityStampAsync(user).WaitAsync(token)).Succeeded).IsTrue();
        var persisted = await observer.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId, token);
        await Assert.That(string.Equals(originalStamp, persisted.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        var retained = await observer.UserExternalLogins.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Binding.ExternalLoginId, token);
        await Assert.That(retained.Id == original.Id && retained.UserId == original.UserId
            && retained.AuthenticationProviderId == original.AuthenticationProviderId
            && retained.ProviderKey == original.ProviderKey).IsTrue();
    }

    private static async Task<Guid> SeedProbeAdministratorAsync(ExploreDbContext seed, Guid actor)
    {
        int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
            && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = actor,
            User = null!,
            RoleId = role,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        seed.PlatformUserRoles.Add(grant);
        await seed.SaveChangesAsync();
        return grant.Id;
    }

    private static async Task SeedProbeProviderAsync(ExploreDbContext seed)
    {
        foreach (var (key, value) in new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Storage.Provider] = "\"s3_compatible\"",
            [GovernanceSettingKeys.Storage.Endpoint] = "\"https://probe.example.test\"",
            [GovernanceSettingKeys.Storage.BucketName] = "\"probe-bucket\"",
            [GovernanceSettingKeys.Storage.Region] = "\"us-east-1\"",
            [GovernanceSettingKeys.Storage.RouteMatrix] = "{\"Version\":1,\"Routes\":[]}"
        })
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else row.Value = value;
        }
        await seed.SaveChangesAsync();
    }

    // Only the external secret source and S3 SDK are substituted. Selection, retained
    // reference capture, real ticket validation, source fences and physical owners are native.
    private sealed class ProbeRemote : ISecretSource
    {
        internal ExploreDbContext? Owner { get; set; }
        internal string Credential { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        internal string Revision { get; set; } = Guid.CreateVersion7().ToString("N");
        internal string? Failure { get; set; }
        internal Func<Task>? AfterVersioning { get; set; }
        internal Func<Task>? AfterPut { get; set; }
        internal Func<Task>? AfterDelete { get; set; }
        internal HashSet<(string Bucket, string Key, string Version)> Objects { get; } = [];
        internal int Puts { get; private set; }
        internal int Deletes { get; private set; }
        public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

        internal void Register(IServiceCollection services)
        {
            services.AddStorageProbeOriginalAction();
            services.AddStorageProbeSourceOwner();
            services.RemoveAll<ISecretSource>();
            services.AddSingleton<ISecretSource>(this);
            services.AddScoped<IRetainedSecretResolver, Explore.Secrets.Services.SecretResolver>();
            var factory = Substitute.For<IStorageProbeClientFactory>();
            factory.Create(Arg.Any<S3Configuration>()).Returns(call =>
                CreateClient(call.Arg<S3Configuration>() ?? throw new InvalidOperationException("Missing probe configuration.")));
            services.RemoveAll<IStorageProbeClientFactory>();
            services.AddSingleton(factory);
        }

        public Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default)
        {
            RequireReleased();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(
                binding.SettingKey, Credential, SourceType, binding.Scope, binding.ScopeId,
                DateTimeOffset.UtcNow, Revision)));
        }
        public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            (await GetSecretAsync(binding, cancellationToken)).IsResolved;

        private IAmazonS3 CreateClient(S3Configuration config)
        {
            RequireReleased();
            if (config.Endpoint != "https://probe.example.test" || config.BucketName != "probe-bucket"
                || config.AccessKeyId != Credential || config.SecretAccessKey != Credential)
                throw new InvalidOperationException("The probe substituted its retained provider or credential.");
            var client = Substitute.For<IAmazonS3>();
            client.GetBucketVersioningAsync(Arg.Any<GetBucketVersioningRequest>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    RequireReleased();
                    if (AfterVersioning is { } callback) await callback();
                    return new GetBucketVersioningResponse
                    {
                        VersioningConfig = new S3BucketVersioningConfig
                        {
                            Status = Failure == "unversioned" ? VersionStatus.Off : VersionStatus.Enabled
                        }
                    };
                });
            client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                RequireReleased();
                var put = call.Arg<PutObjectRequest>();
                ArgumentNullException.ThrowIfNull(put);
                if (put.BucketName != "probe-bucket" || put.IfNoneMatch != "*" || put.Headers.ContentLength != 0)
                    throw new InvalidOperationException("The actual put must be a conditional empty probe.");
                Puts++;
                if (Failure == "foreign-collision")
                {
                    Objects.Add((put.BucketName, put.Key, "foreign-version"));
                    throw new AmazonS3Exception(Credential) { StatusCode = HttpStatusCode.PreconditionFailed };
                }
                Objects.Add((put.BucketName, put.Key, "probe-version"));
                // Another writer replaces the current key after our conditional put;
                // only the returned version, never the current key, belongs to cleanup.
                Objects.Add((put.BucketName, put.Key, "foreign-version"));
                if (AfterPut is { } callback) await callback();
                if (Failure == "ambiguous-put") throw new HttpRequestException(Credential);
                return new PutObjectResponse { VersionId = Failure == "missing-version" ? null : "probe-version" };
            });
            client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                RequireReleased();
                var delete = call.Arg<DeleteObjectRequest>();
                ArgumentNullException.ThrowIfNull(delete);
                Deletes++;
                if (delete.BucketName != "probe-bucket" || delete.VersionId != "probe-version")
                    throw new InvalidOperationException("Cleanup attempted to delete an unowned object.");
                if (Failure == "cleanup-failed") throw new AmazonS3Exception(Credential);
                if (!Objects.Remove((delete.BucketName, delete.Key, delete.VersionId)))
                    throw new InvalidOperationException("Cleanup did not own the acknowledged probe version.");
                if (AfterDelete is { } callback) await callback();
                return new DeleteObjectResponse { VersionId = delete.VersionId };
            });
            return client;
        }

        private void RequireReleased()
        {
            if (Owner?.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("External probe work retained a database transaction.");
        }
    }
}
