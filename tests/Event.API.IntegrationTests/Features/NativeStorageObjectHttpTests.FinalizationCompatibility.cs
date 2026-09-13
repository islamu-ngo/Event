using System.Net;
using System.Net.Http.Json;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.StorageObject.Validators;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    // Same project-owned 2x2 PNG used by StorageContentSignaturePolicySecurityTests.
    private static readonly byte[] CompatibilityPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    [Test]
    [Arguments("authenticated", false)]
    [Arguments("tenant-admin", false)]
    [Arguments("instance-admin", false)]
    [Arguments("authenticated", true)]
    [Arguments("tenant-admin", true)]
    [Arguments("instance-admin", true)]
    public async Task NonEvidenceFinalizationPreservesCerbosForAllPurposesAndOwnerPairs(string principalKind, bool byo)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        await GrantCompatibilityAuthorityAsync(factory, factory.OwnerId, principalKind);
        var transport = new StoragePolicyBoundary();
        await using var selected = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICerbosClient>();
            services.AddSingleton(transport.Client);
            services.RemoveAll<ICerbosClientFactory>();
            services.AddSingleton(transport.Factory);
            services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
        }));
        if (byo)
        {
            using var configurationScope = selected.Services.CreateScope();
            await ConfigureCompatibilityByoAsync(configurationScope.ServiceProvider, factory.OwnerId);
        }
        using var client = selected.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(factory.OwnerId));
        var failures = new List<(string Purpose, string? OwnerKind, HttpStatusCode Status)>();
        var sessionIds = new HashSet<string>();
        long expectedUsedBytes = 0;
        int finalizedCount = 0;
        foreach (string purpose in StorageObjectPurposes.All)
        foreach (bool hasOwnerPair in new[] { false, true })
        {
            bool image = SafeRasterContentPolicy.IsImagePurpose(purpose);
            byte[] bytes = image ? CompatibilityPng : "%PDF-"u8.ToArray();
            var upload = new CreateStorageUploadSessionDto
            {
                ContentType = image ? "image/png" : "application/pdf", Extension = image ? "png" : "pdf",
                OriginalFileName = image ? "image.png" : "document.pdf", ExpectedSizeBytes = bytes.Length,
                Purpose = purpose, Visibility = image ? StorageObjectVisibilities.PublicImage : StorageObjectVisibilities.PrivateOwner,
                OwningResourceKind = hasOwnerPair ? "event" : null,
                OwningResourceId = hasOwnerPair ? Guid.CreateVersion7() : null,
                IdempotencyKey = $"compatibility-{purpose}-{hasOwnerPair}"
            };
            var reserved = await ReserveAsync(client, upload);
            sessionIds.Add(reserved.Id.ToString("D"));
            using var finalized = await PutAsync(client, reserved.Id, bytes);
            if (finalized.StatusCode != HttpStatusCode.OK)
            {
                failures.Add((purpose, upload.OwningResourceKind, finalized.StatusCode));
                continue;
            }
            var result = (await finalized.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;
            expectedUsedBytes += bytes.Length;
            finalizedCount++;
            await Assert.That(result.Status).IsEqualTo(StorageUploadSessionStates.Finalized);
            await Assert.That(result.UsedBytes).IsEqualTo(expectedUsedBytes);
            await Assert.That(result.TotalReservedBytes).IsEqualTo(0);
            await Assert.That(result.Purpose).IsEqualTo(purpose);
            await Assert.That(result.Visibility).IsEqualTo(upload.Visibility);
            using var replay = await PutAsync(client, reserved.Id, bytes);
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await replay.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!.StorageObjectId)
                .IsEqualTo(result.StorageObjectId);
            await Assert.That(factory.WriteCount).IsEqualTo(finalizedCount);
            using var scope = selected.Services.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = FinalizationPrincipal(factory.OwnerId);
            try
            {
                using var content = new MemoryStream(bytes);
                var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>().ResolveAsync(
                    new FinalizeStorageUploadSessionCommand { UploadSessionId = reserved.Id, Content = content, TenantId = factory.OtherTenantId },
                    ResourceKinds.StorageObject, AuthorizationActions.Create, reserved.Id.ToString("D"), null, default);
                await Assert.That(resolved.Facts).IsEqualTo(new StorageObjectCollectionAuthorizationFacts(PlatformDefaults.DefaultTenantId));
                var check = new AuthorizationRequest(ResourceKinds.StorageObject, reserved.Id.ToString("D"), AuthorizationActions.Create, Facts: resolved.Facts);
                await Assert.That((await scope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>().AuthorizeAsync(check)).IsAllowed).IsFalse();
            }
            finally { accessor.HttpContext = null; }
        }
        await Assert.That(failures).IsEmpty();
        await Assert.That(transport.Endpoints.Count > 0).IsEqualTo(byo);
        await Assert.That(finalizedCount).IsEqualTo(12);
        await Assert.That(factory.WriteCount).IsEqualTo(12);
        var finalizationResources = transport.Requests.SelectMany(request => request.Resources)
            .Where(resource => sessionIds.Contains(resource.Resource.Id)).ToArray();
        await Assert.That(finalizationResources.Length).IsEqualTo(24);
        await Assert.That(finalizationResources.All(resource => resource.Resource.Attr["tenantId"].StringValue == PlatformDefaults.DefaultTenantId.ToString("D"))).IsTrue();
        await Assert.That(transport.Requests.All(request => request.Principal.Roles.Contains("islamuevent_authenticated_user"))).IsTrue();
        await Assert.That(transport.Requests.All(request => request.Principal.Attr["isInstanceAdmin"].BoolValue == (principalKind == "instance-admin"))).IsTrue();
        await Assert.That(transport.Requests.All(request =>
            request.Principal.Attr["tenantMemberships"].StructValue.Fields.ContainsKey(PlatformDefaults.DefaultTenantId.ToString("D")) == (principalKind == "tenant-admin"))).IsTrue();
        var document = new CreateStorageUploadSessionDto
        {
            ContentType = "application/pdf", Extension = "pdf", OriginalFileName = "document.pdf", ExpectedSizeBytes = 5,
            Purpose = StorageObjectPurposes.Document, Visibility = StorageObjectVisibilities.PrivateOwner, IdempotencyKey = "ownership-boundary"
        };
        foreach (var incomplete in new[]
        {
            document with { OwningResourceKind = "event", IdempotencyKey = "missing-owner-id" },
            document with { OwningResourceId = Guid.CreateVersion7(), IdempotencyKey = "missing-owner-kind" }
        })
        {
            using var invalid = await client.PostAsJsonAsync(Root + "/upload-sessions", incomplete);
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var guarded = await ReserveAsync(client, document);
        Guid otherUserId;
        using (var scope = selected.Services.CreateScope())
            otherUserId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>())).UserId;
        using var stranger = selected.CreateClient();
        stranger.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(otherUserId));
        using var denied = await PutAsync(stranger, guarded.Id, "%PDF-"u8.ToArray());
        await ProblemAsync(denied, HttpStatusCode.NotFound, FailureCodes.StorageUploadSessionNotFound);
        using var anonymous = selected.CreateClient();
        using var unauthenticated = await PutAsync(anonymous, guarded.Id, "%PDF-"u8.ToArray());
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertFinalizationReservationAsync(factory, guarded.Id, StorageUploadSessionStates.Reserved, 5);
        await Assert.That(factory.WriteCount).IsEqualTo(12);
    }

    [Test]
    [Arguments("authenticated", false)]
    [Arguments("tenant-admin", false)]
    [Arguments("instance-admin", false)]
    [Arguments("authenticated", true)]
    [Arguments("tenant-admin", true)]
    [Arguments("instance-admin", true)]
    public async Task OrganizationTenantReservationsRemainCerbosUnsupportedForEveryPurpose(string principalKind, bool byo)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        await GrantCompatibilityAuthorityAsync(factory, owner.UserId, principalKind);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            var transport = new StoragePolicyBoundary();
            if (byo) await ConfigureCompatibilityByoAsync(scope.ServiceProvider, owner.UserId);
            var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(scope.ServiceProvider, transport.Client, transport.Factory);
            var runtime = ActivatorUtilities.CreateInstance<RuntimeAuthorizationProvider>(scope.ServiceProvider, cerbos,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "cerbos" }));
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var participationId = await db.OrganizationTenants.Where(item => item.OrganizationId == owner.OrganizationId).Select(item => item.Id).SingleAsync();
            var counter = await scope.ServiceProvider.GetRequiredService<IStorageUsageCounterRepository>()
                .GetOrCreateAsync(owner.TenantId, StorageProviders.Local, default);
            long reservedBytes = 0;
            var checks = new List<AuthorizationRequest>();
            foreach (string purpose in StorageObjectPurposes.All)
            {
                bool image = SafeRasterContentPolicy.IsImagePurpose(purpose);
                byte[] bytes = image ? CompatibilityPng : "%PDF-"u8.ToArray();
                var upload = new CreateStorageUploadSessionDto
                {
                    ContentType = image ? "image/png" : "application/pdf", Extension = image ? "png" : "pdf",
                    OriginalFileName = image ? "image.png" : "document.pdf", ExpectedSizeBytes = bytes.Length,
                    Purpose = purpose, Visibility = image ? StorageObjectVisibilities.PublicImage : StorageObjectVisibilities.PrivateOwner,
                    OwningResourceKind = StorageOwningResourceKinds.OrganizationTenant, OwningResourceId = participationId,
                    IdempotencyKey = $"canonical-{purpose}"
                };
                await Assert.That((await new CreateStorageUploadSessionDtoValidator().ValidateAsync(upload)).IsValid).IsTrue();
                var session = new StorageUploadSession
                {
                    Id = Guid.CreateVersion7(), TenantId = owner.TenantId, UserId = owner.UserId, Provider = StorageProviders.Local,
                    ContentType = upload.ContentType, Extension = upload.Extension, SafeDisplayName = upload.OriginalFileName!,
                    Purpose = purpose, Visibility = upload.Visibility, OwningResourceKind = upload.OwningResourceKind, OwningResourceId = participationId,
                    ExpectedSizeBytes = bytes.Length, ReservedBytes = bytes.Length, PolicyMaxUploadBytes = 1048576,
                    Status = StorageUploadSessionStates.Reserved, ExpiresAt = DateTime.UtcNow.AddHours(1), IdempotencyKey = upload.IdempotencyKey
                };
                db.StorageUploadSessions.Add(session);
                counter.Reserve(bytes.Length, 1048576);
                reservedBytes += bytes.Length;
                await db.SaveChangesAsync();
                using var content = new MemoryStream(bytes);
                var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>().ResolveAsync(
                    new FinalizeStorageUploadSessionCommand { UploadSessionId = session.Id, Content = content },
                    ResourceKinds.StorageObject, AuthorizationActions.Create, session.Id.ToString("D"), null, default);
                await Assert.That(resolved.Facts).IsTypeOf<StorageUploadFinalizationFacts>();
                checks.Add(new AuthorizationRequest(ResourceKinds.StorageObject, session.Id.ToString("D"), AuthorizationActions.Create, Facts: resolved.Facts));
                await Assert.That((await runtime.AuthorizeAsync(checks[^1])).IsAllowed).IsFalse();
            }
            await Assert.That((await runtime.AuthorizeBatchAsync(checks)).All(result => !result.IsAllowed)).IsTrue();
            await Assert.That(transport.Requests).IsEmpty();
            await Assert.That(transport.Endpoints.Count > 0).IsEqualTo(byo);
            await Assert.That(counter.ReservedBytes).IsEqualTo(reservedBytes);
            await Assert.That(factory.WriteCount).IsEqualTo(0);
        }
        finally { accessor.HttpContext = null; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnsupportedEvidenceFactsStayDeniedForInstanceAdminOwnerAcrossByoFailures(bool configFailure)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        await GrantCompatibilityAuthorityAsync(factory, owner.UserId, "instance-admin");
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            await Assert.That(await scope.ServiceProvider.GetRequiredService<IAdminContext>().IsInstanceAdminAsync()).IsTrue();
            var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
            await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true", SettingScope.Instance, Guid.Empty, owner.UserId);
            await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\"", SettingScope.Tenant, owner.TenantId, owner.UserId);
            await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"https://cerbos.example.test\"", SettingScope.Tenant, owner.TenantId, owner.UserId);
            bool secretsUnavailable = false;
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (secretsUnavailable) throw new IOException("Secret provider unavailable.");
                return SecretResolutionResult.Unconfigured;
            });
            var transport = new StoragePolicyBoundary();
            var config = ActivatorUtilities.CreateInstance<CerbosConfigResolver>(scope.ServiceProvider, secrets, transport.Factory);
            config.InvalidateCache(owner.TenantId);
            var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(scope.ServiceProvider, transport.Client, transport.Factory);
            var runtime = ActivatorUtilities.CreateInstance<RuntimeAuthorizationProvider>(scope.ServiceProvider, cerbos, config,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "local" }));
            using var content = new MemoryStream("%PDF-"u8.ToArray());
            var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>().ResolveAsync(
                new FinalizeStorageUploadSessionCommand { UploadSessionId = reserved.Id, Content = content },
                ResourceKinds.StorageObject, AuthorizationActions.Create, reserved.Id.ToString("D"), null, default);
            var facts = (StorageUploadFinalizationFacts)resolved.Facts!;
            var finalize = new AuthorizationRequest(ResourceKinds.StorageObject, reserved.Id.ToString("D"), AuthorizationActions.Create, Facts: facts);
            var reserve = new AuthorizationRequest(ResourceKinds.StorageObject, nameof(CreateStorageUploadSessionCommand), AuthorizationActions.Create,
                Facts: new StorageUploadIntentFacts(owner.UserId, owner.TenantId, StorageOwningResourceKinds.OrganizationTenant, facts.OwningResourceId!.Value, owner.OrganizationId));
            var unrelated = new AuthorizationRequest(ResourceKinds.Tenant, owner.TenantId.ToString("D"), AuthorizationActions.Tenants.View,
                Facts: new TenantScopedAuthorizationFacts(owner.TenantId));
            var local = scope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>();
            await Assert.That((await local.AuthorizeAsync(finalize)).IsAllowed).IsTrue();
            await Assert.That((await runtime.AuthorizeAsync(finalize)).IsAllowed).IsFalse();
            await Assert.That((await runtime.AuthorizeBatchAsync([finalize, unrelated, reserve])).Select(result => result.IsAllowed))
                .IsEquivalentTo([false, true, false]);
            transport.Unavailable = !configFailure;
            secretsUnavailable = configFailure;
            config.InvalidateCache(owner.TenantId);
            var failures = new List<string>();
            foreach (var checks in new[] { new[] { finalize, unrelated, reserve }, new[] { finalize }, new[] { reserve } })
            {
                var decisions = await runtime.AuthorizeBatchAsync(checks);
                for (int index = 0; index < checks.Length; index++)
                {
                    bool expected = checks[index] == unrelated;
                    if (decisions[index].IsAllowed != expected) failures.Add(checks[index].ResourceId);
                }
            }
            if ((await runtime.AuthorizeAsync(finalize)).IsAllowed) failures.Add("single-finalization");
            if ((await runtime.AuthorizeAsync(reserve)).IsAllowed) failures.Add("single-reservation");
            await Assert.That(failures).IsEmpty();
            await Assert.That((await local.AuthorizeAsync(unrelated)).IsAllowed).IsTrue();
            await Assert.That(local.SafeMode).IsTrue();
            await Assert.That(transport.Requests.SelectMany(request => request.Resources)
                .All(resource => resource.Resource.Id == unrelated.ResourceId)).IsTrue();
            await AssertFinalizationReservationAsync(factory, reserved.Id, StorageUploadSessionStates.Reserved, 5);
            await Assert.That(factory.WriteCount).IsEqualTo(0);
        }
        finally { accessor.HttpContext = null; }
    }

    private static async Task ConfigureCompatibilityByoAsync(IServiceProvider services, Guid userId)
    {
        var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
        await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true", SettingScope.Instance, Guid.Empty, userId);
        await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\"", SettingScope.Tenant, PlatformDefaults.DefaultTenantId, userId);
        await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"https://cerbos.example.test\"", SettingScope.Tenant, PlatformDefaults.DefaultTenantId, userId);
        services.GetRequiredService<ICerbosConfigResolver>().InvalidateCache(PlatformDefaults.DefaultTenantId);
    }

    private static async Task GrantCompatibilityAuthorityAsync(StorageFactory factory, Guid userId, string principalKind)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (principalKind == "instance-admin")
        {
            var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
            db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = userId, User = null!, RoleId = role.Id, Role = role });
        }
        if (principalKind == "tenant-admin")
        {
            var member = await db.TenantUsers.SingleAsync(item => item.UserId == userId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                TenantUserId = member.Id, TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
            });
        }
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
            GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "1048576", SettingScope.Tenant, PlatformDefaults.DefaultTenantId, userId);
    }

    private sealed class StoragePolicyBoundary
    {
        public ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        public ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        public List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        public List<string> Endpoints { get; } = [];
        public bool Unavailable { get; set; }

        public StoragePolicyBoundary()
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(call =>
            {
                Endpoints.Add(call.Arg<string>() ?? throw new InvalidOperationException("Missing PDP endpoint."));
                return Client;
            });
            Client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
            {
                var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
                Requests.Add(request);
                if (Unavailable) throw new RpcException(new Status(StatusCode.Unavailable, "PDP unavailable."));
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                bool authenticated = request.Principal.Roles.Contains("islamuevent_authenticated_user");
                bool instanceAdmin = authenticated && request.Principal.Attr.TryGetValue("isInstanceAdmin", out var instance) && instance.BoolValue;
                foreach (var resource in request.Resources)
                {
                    bool tenantAdmin = authenticated && resource.Resource.Attr.TryGetValue("tenantId", out var tenant)
                        && request.Principal.Attr.TryGetValue("tenantMemberships", out var memberships)
                        && memberships.StructValue.Fields.ContainsKey(tenant.StringValue);
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                    };
                    foreach (string action in resource.Actions)
                    {
                        // Project-owned storage policy: instance admin *, tenant admin management,
                        // authenticated create. Only the unrelated instance-admin tenant-view canary is supported here.
                        bool allowed = resource.Resource.Kind == ResourceKinds.StorageObject
                            ? instanceAdmin || authenticated && action == AuthorizationActions.Create
                                || tenantAdmin && action is "view" or "update" or "delete" or "download" or "presigned_download"
                            : resource.Resource.Kind == ResourceKinds.Tenant && instanceAdmin && action == AuthorizationActions.Tenants.View;
                        result.Actions.Add(action, allowed ? Cerbos.Api.V1.Effect.Effect.Allow : Cerbos.Api.V1.Effect.Effect.Deny);
                    }
                    response.Results.Add(result);
                }
                return new CheckResourcesResponse(response);
            });
        }
    }
}
