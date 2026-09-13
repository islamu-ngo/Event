using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeStorageObjectHttpTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task EvidenceFinalizationCannotBypassSelectedCerbosOrByoEvenWhenLocalAllows(bool byo, bool unavailable)
    {
        await using var factory = await StorageFactory.CreateAsync(useProductionAuthorization: true);
        var owner = await SeedFinalizationOwnerAsync(factory, alsoTenantAdmin: true);
        using var client = Client(factory, owner.UserId);
        var reserved = await ReserveEvidencePdfAsync(client, owner.OrganizationId);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = FinalizationPrincipal(owner.UserId);
        try
        {
            if (byo)
            {
                var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
                await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true", SettingScope.Instance, Guid.Empty, owner.UserId);
                await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\"", SettingScope.Tenant, owner.TenantId, owner.UserId);
                await settings.SetValueAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"https://cerbos.example.test\"", SettingScope.Tenant, owner.TenantId, owner.UserId);
                scope.ServiceProvider.GetRequiredService<ICerbosConfigResolver>().InvalidateCache(owner.TenantId);
            }
            var grpc = Substitute.For<ICerbosClient>();
            var grpcFactory = Substitute.For<ICerbosClientFactory>();
            grpcFactory.GetOrCreate(Arg.Any<string>()).Returns(grpc);
            var observedResources = new List<string>();
            grpc.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
            {
                var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
                observedResources.AddRange(request.Resources.Select(item => item.Resource.Id));
                if (unavailable) throw new RpcException(new Status(StatusCode.Unavailable, "Boundary unavailable"));
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                foreach (var resource in request.Resources)
                {
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                    };
                    foreach (var action in resource.Actions) result.Actions.Add(action, Cerbos.Api.V1.Effect.Effect.Allow);
                    response.Results.Add(result);
                }
                return new CheckResourcesResponse(response);
            });
            var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(scope.ServiceProvider, grpc, grpcFactory);
            var runtime = ActivatorUtilities.CreateInstance<RuntimeAuthorizationProvider>(scope.ServiceProvider, cerbos,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = byo ? "local" : "cerbos" }));
            using var bytes = new MemoryStream("%PDF-"u8.ToArray());
            var command = new FinalizeStorageUploadSessionCommand { UploadSessionId = reserved.Id, Content = bytes };
            var resolved = await scope.ServiceProvider.GetRequiredService<AuthorizationResourceContextResolver>()
                .ResolveAsync(command, ResourceKinds.StorageObject, AuthorizationActions.Create, reserved.Id.ToString("D"), null, default);
            var check = new AuthorizationRequest(ResourceKinds.StorageObject, reserved.Id.ToString("D"), AuthorizationActions.Create, Facts: resolved.Facts);
            await Assert.That((await scope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>().AuthorizeAsync(check)).IsAllowed).IsTrue();
            await Assert.That((await runtime.AuthorizeAsync(check)).IsAllowed).IsFalse();
            await Assert.That((await cerbos.AuthorizeBatchAsync([check])).Single().IsAllowed).IsFalse();
            await Assert.That(observedResources).IsEmpty();

            var other = new AuthorizationRequest(ResourceKinds.Category, Guid.CreateVersion7().ToString("D"), AuthorizationActions.Create);
            var mixed = await runtime.AuthorizeBatchAsync([check, other]);
            await Assert.That(mixed[0].IsAllowed).IsFalse();
            await Assert.That(mixed[1].IsAllowed).IsEqualTo(!unavailable);
            await Assert.That(observedResources).IsEquivalentTo([other.ResourceId]);
            await Assert.That(factory.WriteCount).IsEqualTo(0);
            await AssertFinalizationReservationAsync(factory, reserved.Id, Explore.Domain.StorageUploadSessionStates.Reserved, 5);
        }
        finally { accessor.HttpContext = null; }
    }
}
