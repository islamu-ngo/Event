using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeStorageObjectHttpTests
{
    private const string Root = "/api/storageobject";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrganizationEvidenceParentUsesOrgAuthorityAndRejectsForeignParticipation(bool alsoTenantAdmin)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        Guid userId;
        Guid organizationId;
        Guid participationId;
        Guid foreignOrganizationId = Guid.CreateVersion7();
        Guid foreignParticipationId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>())
                .IsTypeOf<RuntimeAuthorizationProvider>();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var organization = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
            userId = organization.UserId;
            organizationId = organization.OrganizationId;
            var participation = await db.OrganizationTenants.SingleAsync(item => item.OrganizationId == organizationId);
            participation.ApprovalStatusId = (int)ApprovalStatusEnum.Pending;
            participation.ApprovedAt = null;
            participationId = participation.Id;
            if (alsoTenantAdmin)
            {
                var membership = await db.TenantUsers.SingleAsync(item => item.UserId == userId);
                db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = organization.TenantId, Tenant = null!,
                    TenantUserId = membership.Id, TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
                });
            }
            var foreignOrganization = new Organization
            {
                Id = foreignOrganizationId,
                Pii = new OrganizationPii { OrganizationId = foreignOrganizationId, FullName = "Foreign evidence owner" },
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            var foreignParticipation = new OrganizationTenant
            {
                Id = foreignParticipationId, TenantId = factory.OtherTenantId, Tenant = null!,
                OrganizationId = foreignOrganizationId, Organization = foreignOrganization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending, ApprovalStatus = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            db.OrganizationTenants.Add(foreignParticipation);
            db.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(), TenantId = factory.OtherTenantId, Tenant = null!,
                OrganizationTenantId = foreignParticipationId, OrganizationTenant = foreignParticipation,
                UserId = userId, User = null!, RoleId = (int)RoleEnum.OrgAdmin, Role = null!
            });
            await db.SaveChangesAsync();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<IAdminContext>()
                .IsInstanceAdminAsync(userId)).IsFalse();
        }

        using var client = Client(factory, userId);
        var upload = new CreateOrganizationTenantEvidenceUploadSessionDto
        {
            FileName = " evidence.pdf ", ContentType = "application/pdf", ExpectedSizeBytes = 5
        };
        using var accepted = await client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/legitimacy-evidence/upload-session", upload);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var session = (await accepted.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
        await Assert.That(session.UserId).IsEqualTo(userId);
        await Assert.That(session.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(session.Purpose).IsEqualTo(StorageObjectPurposes.Document);
        await Assert.That(session.Visibility).IsEqualTo(StorageObjectVisibilities.PrivateOwner);
        await Assert.That(session.SafeDisplayName).IsEqualTo("evidence.pdf");
        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Reserved);
        await Assert.That(session.TotalReservedBytes).IsEqualTo(5);

        // The same principal has an OrgAdmin membership in the foreign tenant, not in the current scope.
        using var foreignParent = await client.PostAsJsonAsync(
            $"/api/organizations/{foreignOrganizationId}/legitimacy-evidence/upload-session", upload);
        await Assert.That(foreignParent.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var forgedChild = await client.PostAsJsonAsync(Root + "/upload-sessions", new CreateStorageUploadSessionDto
        {
            ExpectedSizeBytes = 5, ContentType = "application/pdf", OriginalFileName = "foreign.pdf", Extension = "pdf",
            Purpose = StorageObjectPurposes.Document, Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.OrganizationTenant, OwningResourceId = foreignParticipationId,
            IdempotencyKey = "foreign-evidence"
        });
        await Assert.That(forgedChild.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var scope = factory.Services.CreateScope())
        {
            var persisted = await scope.ServiceProvider.GetRequiredService<IStorageUploadSessionRepository>()
                .GetActiveByIdAsync(session.Id, default);
            await Assert.That(persisted!.OwningResourceKind).IsEqualTo(StorageOwningResourceKinds.OrganizationTenant);
            await Assert.That(persisted.OwningResourceId).IsEqualTo(participationId);
            var counters = await scope.ServiceProvider.GetRequiredService<IStorageUsageCounterRepository>()
                .GetByTenantAsync(PlatformDefaults.DefaultTenantId, default);
            await Assert.That(counters.Single().ReservedBytes).IsEqualTo(5);
            await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .StorageUploadSessions.CountAsync()).IsEqualTo(1);
        }
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        await Assert.That(factory.Objects).IsEmpty();
        await Assert.That(factory.Signings).IsEmpty();
    }

    [Test]
    public async Task UploadReplayQuotaFinalizeReadUpdateCapabilityAndDeleteKeepTheirEffects()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        var reserved = await ReserveAsync(client, Upload("first"));
        await Assert.That(reserved.TotalReservedBytes).IsEqualTo(5);
        var replay = await ReserveAsync(client, Upload("first"));
        await Assert.That(replay.Id).IsEqualTo(reserved.Id);
        await Assert.That(replay.TotalReservedBytes).IsEqualTo(5);
        using (var quota = await client.PostAsJsonAsync(Root + "/upload-sessions", Upload("over-quota", 8)))
            await ProblemAsync(quota, HttpStatusCode.UnprocessableEntity, FailureCodes.QuotaExceeded);

        var finalized = await FinalizeAsync(client, reserved.Id);
        await Assert.That(finalized.Status).IsEqualTo(StorageUploadSessionStates.Finalized);
        await Assert.That(finalized.UsedBytes).IsEqualTo(5);
        await Assert.That(finalized.TotalReservedBytes).IsEqualTo(0);
        await Assert.That((await FinalizeAsync(client, reserved.Id)).StorageObjectId).IsEqualTo(finalized.StorageObjectId);
        await Assert.That(factory.WriteCount).IsEqualTo(1);
        Guid id = finalized.StorageObjectId!.Value;
        using (var response = await client.GetAsync($"{Root}/{id}/content"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo("hello"u8.ToArray());
            await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        }
        await Assert.That(factory.DisposedReads).IsEqualTo(1);
        using (var updated = await client.PatchAsJsonAsync($"{Root}/{id}", new UpdateStorageObjectDto
        {
            Metadata = new() { FullName = "renamed.txt", SafeDisplayName = "renamed.txt" }
        }))
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var detail = await client.GetAsync($"{Root}/{id}"))
        {
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await detail.Content.ReadAsStringAsync()).Contains("renamed.txt");
        }
        using (var list = await client.GetAsync(Root))
        {
            await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await list.Content.ReadAsStringAsync()).DoesNotContain("tenants/");
        }
        using (var issued = await client.GetAsync($"{Root}/{id}/presigned-url?expirationMinutes=15"))
        {
            await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(issued.Headers.CacheControl!.NoStore).IsTrue();
            var capability = (await issued.Content.ReadFromJsonAsync<PresignedDownloadUrlResponseDto>())!;
            await Assert.That(capability.PresignedUrl).IsEqualTo(factory.Capability);
            await Assert.That(capability.ExpiresInMinutes).IsEqualTo(15);
            await Assert.That(capability.ObjectKey).IsEqualTo(string.Empty);
        }
        await Assert.That(factory.Signings.Single().Name).IsEqualTo("renamed.txt");
        await Assert.That(factory.Signings.Single().Minutes).IsEqualTo(15);
        using (var canceled = await client.DeleteAsync($"{Root}/upload-sessions/{reserved.Id}"))
            await ProblemAsync(canceled, HttpStatusCode.Conflict, FailureCodes.StorageUploadSessionFinalized);
        using (var deleted = await client.DeleteAsync($"{Root}/{id}"))
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(factory.Objects).IsEmpty();
        using (var missing = await client.GetAsync($"{Root}/{id}/content"))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task OwnerAndTenantFencesDoNotConsumeAnotherSessionsReservation()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        using var stranger = Client(factory, Guid.CreateVersion7());
        var reserved = await ReserveAsync(owner, Upload("owner"));
        using (var denied = await stranger.DeleteAsync($"{Root}/upload-sessions/{reserved.Id}"))
            await ProblemAsync(denied, HttpStatusCode.NotFound, FailureCodes.StorageUploadSessionNotFound);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var result = await scope.ServiceProvider.GetRequiredService<
                ICommandHandler<CancelStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>>()
                .ExecuteAsync(new() { UploadSessionId = reserved.Id, TenantId = PlatformDefaults.DefaultTenantId }, default);
            await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.StorageUploadSessionNotFound);
        }
        await Assert.That((await ReserveAsync(owner, Upload("owner"))).TotalReservedBytes).IsEqualTo(5);
        using (var canceled = await owner.DeleteAsync($"{Root}/upload-sessions/{reserved.Id}"))
        {
            await Assert.That(canceled.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var result = (await canceled.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!;
            await Assert.That(result.Id!.TotalReservedBytes).IsEqualTo(0);
        }
        using (var replay = await owner.DeleteAsync($"{Root}/upload-sessions/{reserved.Id}"))
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReserveAsync(owner, Upload("all-quota", 12))).TotalReservedBytes).IsEqualTo(12);
        await Assert.That(factory.Objects).IsEmpty();
    }

    [Test]
    public async Task InvalidContentAndProviderFailureReleaseQuotaWithoutPublishingMetadata()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var client = Client(factory, factory.OwnerId);
        var pdf = await ReserveAsync(client, Upload("invalid-pdf") with
        {
            ContentType = "application/pdf", OriginalFileName = "file.pdf", Extension = "pdf"
        });
        using (var invalid = await PutAsync(client, pdf.Id, "hello"u8.ToArray()))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, FailureCodes.StorageUploadContentSignatureMismatch);
        await Assert.That(factory.WriteCount).IsEqualTo(0);
        var failed = await ReserveAsync(client, Upload("failed-provider"));
        factory.FailWrite = true;
        using (var unavailable = await PutAsync(client, failed.Id, "hello"u8.ToArray()))
            await ProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, FailureCodes.StorageUploadWriteFailed);
        await Assert.That(factory.Objects).IsEmpty();
        await Assert.That((await ReserveAsync(client, Upload("after-failures", 12))).TotalReservedBytes).IsEqualTo(12);
    }

    [Test]
    public async Task CapabilityAuthorizationAndNullableReadsFailClosedBeforeProviderEffects()
    {
        await using var factory = await StorageFactory.CreateAsync();
        using var owner = Client(factory, factory.OwnerId);
        var reserved = await ReserveAsync(owner, Upload("capability"));
        Guid id = (await FinalizeAsync(owner, reserved.Id)).StorageObjectId!.Value;
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.GetAsync($"{Root}/{id}/presigned-url"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        factory.DeniedAction = AuthorizationActions.StorageObjects.PresignedDownload;
        using (var denied = await owner.GetAsync($"{Root}/{id}/presigned-url"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        factory.DeniedAction = null;
        using (var invalid = await owner.GetAsync($"{Root}/{id}/presigned-url?expirationMinutes=0"))
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var stranger = Client(factory, Guid.CreateVersion7());
        using (var privateOwner = await stranger.GetAsync($"{Root}/{id}/presigned-url"))
            await Assert.That(privateOwner.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var notPublic = await anonymous.GetAsync($"{Root}/{id}/public"))
            await Assert.That(notPublic.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var scope = factory.Services.CreateScope())
        {
            await factory.Services.ValidateNativeOperationsDeepAsync();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetStorageObjectDetailsRequest, StorageObjectDto?>>()
                .QueryAsync(new() { Id = id, TenantId = PlatformDefaults.DefaultTenantId }, default);
            await Assert.That(result).IsNull();
        }
        await Assert.That(factory.Signings).IsEmpty();
        await Assert.That(factory.DisposedReads).IsEqualTo(0);
    }

    private static CreateStorageUploadSessionDto Upload(string key, long size = 5) => new()
    {
        ExpectedSizeBytes = size, ContentType = "text/plain", OriginalFileName = "file.txt", Extension = "txt",
        Purpose = StorageObjectPurposes.Attachment, Visibility = StorageObjectVisibilities.PrivateOwner, IdempotencyKey = key
    };

    private static HttpClient Client(StorageFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
    }

    private static async Task<StorageUploadSessionDto> ReserveAsync(HttpClient client, CreateStorageUploadSessionDto upload)
    {
        using var response = await client.PostAsJsonAsync(Root + "/upload-sessions", upload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
    }

    private static async Task<StorageUploadSessionDto> FinalizeAsync(HttpClient client, Guid id)
    {
        using var response = await PutAsync(client, id, "hello"u8.ToArray());
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await client.PutAsync($"{Root}/upload-sessions/{id}/content", content);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        // StorageObjectController's existing Produces metadata selects JSON for mapped ProblemDetails.
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private sealed class StorageFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _database = Path.Combine(Path.GetTempPath(), $"native-storage-{Guid.CreateVersion7():N}.db");
        public Guid OwnerId { get; private set; }
        public Guid OtherTenantId { get; private set; }
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<(string Name, int Minutes)> Signings { get; } = [];
        public string Capability { get; } = "https://storage.example.test/download?capability=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public string? DeniedAction { get; set; }
        public bool FailWrite { get; set; }
        public int WriteCount { get; private set; }
        public int DisposedReads { get; private set; }

        public static async Task<StorageFactory> CreateAsync(bool useProductionAuthorization = false)
        {
            var factory = new StorageFactory();
            if (useProductionAuthorization)
            {
                factory.AdditionalConfiguration["Authorization:Provider"] = "local";
            }
            else
            {
                factory.AuthorizationProviderOverride = new StubAuthorizationProvider
                {
                    CheckPredicate = request => request.ResourceKind == ResourceKinds.StorageObject && request.Action != factory.DeniedAction
                };
            }
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using (var db = new ExploreDbContext(options.Options))
                {
                    await db.Database.EnsureCreatedAsync();
                    await SqliteDatabaseInitializer.InitializeAsync(db, default);
                }
                using var scope = factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                factory.OwnerId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
                factory.OtherTenantId = (await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context)).TenantId;
                var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
                await settings.SetValueAsync(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "12",
                    SettingScope.Tenant, PlatformDefaults.DefaultTenantId, factory.OwnerId);
                await settings.SetValueAsync(GovernanceSettingKeys.Storage.Provider, "\"local\"",
                    SettingScope.Tenant, PlatformDefaults.DefaultTenantId, factory.OwnerId);
                return factory;
            }
            catch { await factory.DisposeAsync(); throw; }
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _database
            });
            options.UseSnakeCaseNamingConvention();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
                var storage = Substitute.For<IFileStorageProvider>();
                storage.Provider.Returns(StorageProviders.Local);
                storage.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    var input = call.Arg<FileStorageWriteInput>() ?? throw new ArgumentException("Missing storage write input.");
                    if (FailWrite) throw new IOException("Boundary write failure.");
                    if (input.ObjectKey is null || !input.ObjectKey.StartsWith($"tenants/{input.TenantId:N}/", StringComparison.Ordinal))
                        throw new InvalidOperationException("Upload destination was not tenant-bound.");
                    using var bytes = new MemoryStream();
                    await input.Content.CopyToAsync(bytes, call.Arg<CancellationToken>());
                    byte[] value = bytes.ToArray();
                    Objects.Add(input.ObjectKey, value);
                    WriteCount++;
                    return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey, value.Length,
                        input.ContentType, Convert.ToHexString(SHA256.HashData(value)));
                });
                storage.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var input = call.Arg<FileStorageReadInput>() ?? throw new ArgumentException("Missing storage read input.");
                    byte[] value = Objects[input.ObjectKey];
                    return new FileStorageReadResult(new TrackedStream(value, () => DisposedReads++), "text/plain",
                        value.Length, DateTimeOffset.UtcNow);
                });
                storage.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var input = call.Arg<FileStorageDeleteInput>() ?? throw new ArgumentException("Missing storage delete input.");
                    string key = input.ObjectKey;
                    return new FileStorageDeleteResult(StorageProviders.Local, key, Objects.Remove(key));
                });
                var resolver = Substitute.For<IFileStorageProviderResolver>();
                resolver.GetRequired(StorageProviders.Local).Returns(storage);
                services.RemoveAll<IFileStorageProviderResolver>();
                services.AddSingleton(resolver);
                var signing = Substitute.For<IObjectStorageService>();
                signing.GeneratePresignedDownloadUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(call =>
                {
                    if (!Objects.ContainsKey(call.ArgAt<string>(0))) throw new FileNotFoundException();
                    Signings.Add((call.ArgAt<string>(1), call.Arg<int>()));
                    return Capability;
                });
                services.RemoveAll<IObjectStorageService>();
                services.AddSingleton(signing);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(_database);
            File.Delete(_database + "-wal");
            File.Delete(_database + "-shm");
        }
    }

    private sealed class TrackedStream(byte[] bytes, Action disposed) : MemoryStream(bytes, writable: false)
    {
        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; disposed(); }
            base.Dispose(disposing);
        }
    }
}
