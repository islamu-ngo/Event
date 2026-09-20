using System.Net;
using System.Net.Http.Json;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Application.Contracts.Operations;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    [Arguments("quarantine", false)]
    [Arguments("delete", false)]
    [Arguments("restrict-visibility", false)]
    [Arguments("erase-creator", false)]
    [Arguments("quarantine", true)]
    [Arguments("delete", true)]
    [Arguments("restrict-visibility", true)]
    [Arguments("erase-creator", true)]
    public async Task DownloadReReadsCommittedMetadataAfterAwaitedAuthorization(string mutation, bool manualImport)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        using var ownerClient = Client(factory, owner.UserId);
        Guid objectId = await FinalizeDownloadPdfAsync(ownerClient, owner.OrganizationId);
        Guid eventId = Guid.Empty;
        Guid bindingId = Guid.Empty;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == objectId);
            if (mutation == "restrict-visibility")
            {
                document.Visibility = StorageObjectVisibilities.AuthenticatedTenant;
                document.CreatedBy = factory.OwnerId;
            }
            if (manualImport)
            {
                document.Purpose = StorageObjectPurposes.Attachment;
                document.OwningResourceKind = null;
                document.OwningResourceId = null;
                document.ContentType = "text/csv";
                document.Extension = "csv";
                document.SafeDisplayName = "import.csv";
                byte[] csv = "column\nrow"u8.ToArray();
                factory.Objects[document.ObjectKey!] = csv;
                document.Size = csv.Length;
                (eventId, bindingId) = await SeedManualImportBindingAsync(db, owner.TenantId, owner.ActorId);
            }
            await db.SaveChangesAsync();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grpc = Substitute.For<ICerbosClient>();
        grpc.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(async call =>
        {
            var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions)
                {
                    bool subject = request.Principal.Attr["userId"].StringValue == owner.UserId.ToString("D");
                    bool storage = resource.Resource.Kind == ResourceKinds.StorageObject && resource.Resource.Id == objectId.ToString("D")
                        && action == AuthorizationActions.StorageObjects.Download;
                    bool allowed = subject && (storage
                        ? resource.Resource.Attr["lifecycleState"].StringValue == StorageObjectLifecycleStates.Active
                            && (resource.Resource.Attr["visibility"].StringValue == StorageObjectVisibilities.AuthenticatedTenant
                                || resource.Resource.Attr["createdBy"].StringValue == owner.UserId.ToString("D"))
                        : resource.Resource.Kind == ResourceKinds.Event && resource.Resource.Id == eventId.ToString("D")
                            && action == AuthorizationActions.Events.ManageRegistrationChannels);
                    if (storage && allowed)
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(TimeSpan.FromSeconds(60));
                    }
                    result.Actions.Add(action, allowed ? Cerbos.Api.V1.Effect.Effect.Allow : Cerbos.Api.V1.Effect.Effect.Deny);
                }
                response.Results.Add(result);
            }
            return new CheckResourcesResponse(response);
        });
        var grpcFactory = Substitute.For<ICerbosClientFactory>();
        grpcFactory.GetOrCreate(Arg.Any<string>()).Returns(grpc);
        await using var selected = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICerbosClient>(); services.AddSingleton(grpc);
            services.RemoveAll<ICerbosClientFactory>(); services.AddSingleton(grpcFactory);
            services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
        }));
        using var client = selected.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(owner.UserId));
        using var consumerScope = selected.Services.CreateScope();
        var accessor = consumerScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        Task<HttpResponseMessage>? http = null;
        Task<BaseCommandResponse<Guid>>? import = null;
        if (manualImport)
        {
            accessor.HttpContext = FinalizationPrincipal(owner.UserId);
            import = consumerScope.ServiceProvider.GetRequiredService<ICommandHandler<QueueManualRegistrationProviderImportCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new QueueManualRegistrationProviderImportCommand(
                    owner.TenantId, eventId, bindingId, objectId.ToString("D"), "metadata-race"));
        }
        else http = client.GetAsync($"{Root}/{objectId}/content");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            using var mutationScope = factory.Services.CreateScope();
            var db = mutationScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == objectId);
            switch (mutation)
            {
                case "quarantine": document.LifecycleState = StorageObjectLifecycleStates.Quarantined; break;
                case "delete": document.IsDeleted = true; break;
                case "restrict-visibility": document.Visibility = StorageObjectVisibilities.PrivateOwner; break;
                case "erase-creator": document.CreatedBy = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            await db.SaveChangesAsync();
        }
        finally { release.TrySetResult(); }
        try
        {
            if (manualImport)
            {
                var result = await import!.WaitAsync(TimeSpan.FromSeconds(60));
                await Assert.That(result.FailureCode).IsEqualTo("registration_provider_manual_import_file_invalid");
            }
            else
            {
                using var result = await http!.WaitAsync(TimeSpan.FromSeconds(60));
                await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            }
            await Assert.That(factory.DisposedReads).IsEqualTo(0);
            await consumerScope.ServiceProvider.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local)
                .DidNotReceive().OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
        }
        finally { accessor.HttpContext = null; }
    }

    [Test]
    [Arguments(false, StorageObjectVisibilities.PrivateOwner)]
    [Arguments(true, StorageObjectVisibilities.PrivateOwner)]
    [Arguments(false, StorageObjectVisibilities.AuthenticatedTenant)]
    [Arguments(true, StorageObjectVisibilities.AuthenticatedTenant)]
    [Arguments(false, StorageObjectVisibilities.PublicImage)]
    [Arguments(true, StorageObjectVisibilities.PublicImage)]
    public async Task ExactDownloadOutageCannotGrantInstanceOwnerWithoutTenantAuthority(bool configFailure, string visibility)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory);
        await GrantCompatibilityAuthorityAsync(factory, owner.UserId, "instance-admin");
        using var ownerClient = Client(factory, owner.UserId);
        Guid objectId = await FinalizeDownloadPdfAsync(ownerClient, owner.OrganizationId);
        using (var metadataScope = factory.Services.CreateScope())
        {
            var db = metadataScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var document = await db.StorageObjects.SingleAsync(item => item.Id == objectId);
            document.Visibility = visibility;
            if (visibility == StorageObjectVisibilities.PublicImage)
            {
                document.Purpose = StorageObjectPurposes.EventImage;
                document.ContentType = "image/png";
                document.Extension = "png";
                document.FileTypeId = (int)FileTypeEnum.Image;
                document.Size = CompatibilityPng.Length;
                factory.Objects[document.ObjectKey!] = CompatibilityPng;
            }
            await db.SaveChangesAsync();
        }
        bool unavailable = false;
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (unavailable && configFailure) throw new IOException("Secret provider unavailable.");
            return SecretResolutionResult.Unconfigured;
        });
        var grpc = Substitute.For<ICerbosClient>();
        grpc.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            if (unavailable && !configFailure) throw new RpcException(new Status(StatusCode.Unavailable, "PDP unavailable."));
            var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions)
                {
                    // A deliberately restrictive selected policy denies downloads. Its emergency
                    // tenant/user view canaries follow the existing instance-admin policy.
                    bool allowed = request.Principal.Attr["isInstanceAdmin"].BoolValue
                        && resource.Resource.Kind is ResourceKinds.Tenant or ResourceKinds.User && action == AuthorizationActions.View;
                    result.Actions.Add(action, allowed ? Cerbos.Api.V1.Effect.Effect.Allow : Cerbos.Api.V1.Effect.Effect.Deny);
                }
                response.Results.Add(result);
            }
            return new CheckResourcesResponse(response);
        });
        var grpcFactory = Substitute.For<ICerbosClientFactory>();
        grpcFactory.GetOrCreate(Arg.Any<string>()).Returns(grpc);
        await using var selected = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICerbosClient>(); services.AddSingleton(grpc);
            services.RemoveAll<ICerbosClientFactory>(); services.AddSingleton(grpcFactory);
            services.RemoveAll<ISecretResolver>(); services.AddSingleton(secrets);
            services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "local");
        }));
        using var scope = selected.Services.CreateScope();
        await ConfigureCompatibilityByoAsync(scope.ServiceProvider, owner.UserId);
        using var client = selected.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(owner.UserId));
        using (var healthy = await client.GetAsync($"{Root}/{objectId}/content"))
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        unavailable = true;
        scope.ServiceProvider.GetRequiredService<ICerbosConfigResolver>().InvalidateCache(owner.TenantId);
        var failures = new List<string>();
        using (var outage = await client.GetAsync($"{Root}/{objectId}/content"))
            if (outage.StatusCode != HttpStatusCode.Forbidden) failures.Add("http-download");
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminContext>();
            await Assert.That(await admin.IsInstanceAdminAsync()).IsTrue();
            await Assert.That(await admin.IsTenantAdminAsync(owner.TenantId)).IsFalse();
            var query = new GetStorageObjectContentRequest { StorageObjectId = objectId, TenantId = owner.TenantId };
            var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>().ResolveAsync(query,
                ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.Download, objectId.ToString("D"), null, default);
            var facts = (PersistedStorageObjectAuthorizationFacts)resolved.Facts!;
            var download = new AuthorizationRequest(ResourceKinds.StorageObject, objectId.ToString("D"), AuthorizationActions.StorageObjects.Download, Facts: facts);
            var tenantView = new AuthorizationRequest(ResourceKinds.Tenant, owner.TenantId.ToString("D"), AuthorizationActions.View, Facts: new TenantScopedAuthorizationFacts(owner.TenantId));
            var userView = new AuthorizationRequest(ResourceKinds.User, owner.UserId.ToString("D"), AuthorizationActions.View);
            var runtime = scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>();
            foreach (var checks in new[] { new[] { download, tenantView, userView }, new[] { download } })
            {
                var decisions = await runtime.AuthorizeBatchAsync(checks);
                for (int index = 0; index < checks.Length; index++)
                    if (decisions[index].IsAllowed != (checks[index] != download)) failures.Add(checks[index].ResourceKind);
            }
            if ((await runtime.AuthorizeAsync(download)).IsAllowed) failures.Add("single-download");
            await Assert.That(failures).IsEmpty();
            await Assert.That(factory.DisposedReads).IsEqualTo(0);
            await scope.ServiceProvider.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local)
                .DidNotReceive().OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
            // An actual tenant grant is a different, pre-existing emergency authority.
            await GrantCompatibilityAuthorityAsync(factory, owner.UserId, "tenant-admin");
            await Assert.That(await admin.IsTenantAdminAsync(owner.TenantId)).IsTrue();
            await Assert.That((await runtime.AuthorizeAsync(download)).IsAllowed).IsTrue();
        }
        finally { accessor.HttpContext = null; }
    }

    private static async Task<(Guid EventId, Guid BindingId)> SeedManualImportBindingAsync(ExploreDbContext db, Guid tenantId, Guid actorId)
    {
        var now = DateTime.UtcNow;
        var target = new EventBuilder().WithId(Guid.CreateVersion7()).WithTenantId(tenantId).WithActorId(actorId).Build();
        target.OrganizerActorId = actorId;
        var form = RegistrationForm.Create(tenantId, target.Id, "registration", "manual-import", "Manual import", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var connection = RegistrationProviderConnection.Create(tenantId, "Manual import", RegistrationProviderKindEnum.ExternalApi,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "manual", "hosted", "v1", "v1", "test-revision",
            "https://forms.example.test", "https://forms.example.test", "workspace", null, null, now);
        var binding = RegistrationProviderBinding.Create(tenantId, connection.Id, form.Id, version.Id,
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
            RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, now);
        binding.SetDraftProvisionedSurvey("survey", null);
        binding.AddCapability(RegistrationProviderCapability.Create(binding, "manual", "hosted", "v1", "v1", "test-revision", RegistrationProviderCapabilityCodes.Manual));
        db.AddRange(target, form, connection, binding);
        await db.SaveChangesAsync();
        return (target.Id, binding.Id);
    }
}
