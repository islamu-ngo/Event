using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Substitutes only external secret material and the remote storage provider.</summary>
internal sealed class StorageProbeNativeRemote(PostgreSqlContainerFixture postgres) : ISecretSource
{
    internal ExploreDbContext? Owner { get; set; }
    internal string Credential { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string Revision { get; set; } = Guid.CreateVersion7().ToString("N");
    internal Func<Task>? AfterVersioning { get; set; }
    internal Func<Task>? AfterPut { get; set; }
    internal Func<Task>? AfterDelete { get; set; }
    internal Func<Task>? DuringSecretResolution { get; set; }
    internal int Puts { get; private set; }
    internal int Deletes { get; private set; }
    internal int SecretReads { get; private set; }
    internal HashSet<(string Key, string Version)> Objects { get; } = [];
    public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

    internal void Register(IServiceCollection services)
    {
        services.AddStorageProbeOriginalAction();
        services.AddStorageProbeSourceOwner();
        services.RemoveAll<ISecretSource>();
        services.AddSingleton<ISecretSource>(this);
        services.RemoveAll<IRetainedSecretResolver>();
        services.AddScoped<IRetainedSecretResolver, Explore.Infrastructure.Secrets.SecretResolver>();
        services.RemoveAll<IStorageProbeClientFactory>();
        var factory = Substitute.For<IStorageProbeClientFactory>();
        factory.Create(Arg.Any<S3Configuration>()).Returns(call =>
            Create(call.Arg<S3Configuration>() ?? throw new InvalidOperationException("Missing original configuration.")));
        services.AddSingleton(factory);
    }

    public async Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RequireReleasedAsync();
        SecretReads++;
        if (DuringSecretResolution is { } callback) await callback();
        return SecretResolutionResult.Resolved(new ResolvedSecret(binding.SettingKey, Credential,
            SourceType, binding.Scope, binding.ScopeId, DateTimeOffset.UtcNow, Revision));
    }

    public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
        (await GetSecretAsync(binding, cancellationToken)).IsResolved;

    private IAmazonS3 Create(S3Configuration config)
    {
        if (Owner?.Database.CurrentTransaction is not null || config.AccessKeyId != Credential
            || config.SecretAccessKey != Credential || config.Endpoint != "https://probe.example.test"
            || config.BucketName != "probe-bucket")
            throw new InvalidOperationException("The native probe replaced its retained configuration.");
        var client = Substitute.For<IAmazonS3>();
        client.GetBucketVersioningAsync(Arg.Any<GetBucketVersioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await RequireReleasedAsync();
                if (AfterVersioning is { } callback) await callback();
                return new GetBucketVersioningResponse
                {
                    VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
                };
            });
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await RequireReleasedAsync();
                var request = call.Arg<PutObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                if (request.BucketName != "probe-bucket" || request.IfNoneMatch != "*" || request.Headers.ContentLength != 0)
                    throw new InvalidOperationException("The native probe changed its physical write target.");
                Puts++;
                Objects.Add((request.Key, "owned-version"));
                Objects.Add((request.Key, "replacement-version"));
                if (AfterPut is { } callback) await callback();
                return new PutObjectResponse { VersionId = "owned-version" };
            });
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await RequireReleasedAsync();
                var request = call.Arg<DeleteObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                if (request.BucketName != "probe-bucket" || request.VersionId != "owned-version"
                    || !Objects.Remove((request.Key, request.VersionId)))
                    throw new InvalidOperationException("Native cleanup substituted its acknowledged object version.");
                Deletes++;
                if (AfterDelete is { } callback) await callback();
                return new DeleteObjectResponse { VersionId = request.VersionId };
            });
        return client;
    }

    private async Task RequireReleasedAsync()
    {
        if (Owner is null || Owner.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("External provider work retained a source transaction.");
        await using var contender = postgres.CreateDbContext();
        await using var transaction = await contender.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(contender);
        // NOWAIT is the lock observation, not a timing assertion or a polling loop.
        foreach (string anchor in new[] { PostgresStorageProbeSourceContract.SourceTable,
            "policy_source_set", "human_principal_source_epoch", "human_support_access_source_epoch" })
            await contender.Database.ExecuteSqlRawAsync($"SELECT id FROM {names.Name(anchor)} WHERE id = 1 FOR UPDATE NOWAIT");
        await transaction.RollbackAsync();
    }
}
