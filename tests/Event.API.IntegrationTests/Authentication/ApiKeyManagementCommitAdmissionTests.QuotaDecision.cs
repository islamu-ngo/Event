using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class ApiKeyManagementCommitAdmissionTests
{
    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("principal_changed_after_decision")]
    public async Task QuotaPdpRunsOutsideOwnerAndOriginalInputsAreRequiredInside(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        var query = new GetExternalApiKeyUsageReportRequest
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            From = new(2026, 1, 1),
            To = new(2026, 1, 31)
        };
        Guid grantId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = await seed.TenantUsers.SingleAsync(row => row.UserId == fixture.Binding.LocalSubjectId);
            var grant = new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUserRoleGrants.Add(grant);
            grantId = grant.Id;
            seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = keyId,
                ExternalApiKey = null!,
                PeriodStart = query.From,
                PeriodEnd = query.To,
                RequestCount = 9,
                CreditsUsed = 3
            });
            await seed.SaveChangesAsync();
        }
        var sent = new List<Cerbos.Api.V1.Request.CheckResourcesRequest>();
        bool bodyCompleted = false;
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IHumanApiKeyManagementPreparation>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                var transport = new QuotaTransport(context, sent, change == "deny" ? Effect.Deny : Effect.Allow,
                    async () =>
                    {
                        if (change == "principal_changed_after_decision")
                            await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                                    fixture.Clock.GetUtcNow().UtcDateTime));
                    });
                var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://quota-pdp.invalid:3593" });
                return new HumanApiKeyManagementPreparation(
                    provider.GetRequiredService<IHumanCommitScopePreparation>(),
                    provider.GetRequiredService<IHttpContextAccessor>(),
                    provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
                    provider.GetRequiredService<IMachinePrincipalAccessor>(),
                    provider.GetRequiredService<ITenantContext>(), fixture.Clock,
                    ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
                        provider, transport.Client, transport.Factory, options),
                    Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "cerbos" }), options,
                    () => provider.GetRequiredService<IHumanPrincipalSourceFence>(),
                    () => provider.GetRequiredService<IHumanAuthorizationRouteSource>(),
                    () => provider.GetRequiredService<IHumanSupportAccessSourceFactory>(),
                    () => provider.GetRequiredService<IPolicySourceSetFence>(),
                    () => provider.GetRequiredService<IHumanActionCommitFinalizer>());
            });
            services.AddScoped<ISerializableAttemptOwner>(provider => new AfterBody(
                (ISerializableAttemptOwner)new EfCoreUnitOfWork(provider.GetRequiredService<ExploreDbContext>()),
                _ =>
                {
                    bodyCompleted = true;
                    return Task.CompletedTask;
                }));
        }));
        await using var request = new Request(host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var execution = request.Services.GetRequiredService<IExternalApiKeyTenantUsageReader>().ReadAsync(query, default);
        if (change == "allow")
            await Assert.That((await execution).Single().TotalRequestCount).IsEqualTo(9L);
        else if (change == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(bodyCompleted).IsEqualTo(change == "allow");
        var resource = sent.Single().Resources.Single();
        await Assert.That(resource.Resource.Kind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(resource.Resource.Id).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
        await Assert.That(resource.Actions.Single()).IsEqualTo(AuthorizationActions.View);
    }

    /// <summary>Only the external PDP transport is replaced; owner and source fences remain real.</summary>
    private sealed class QuotaTransport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();

        internal QuotaTransport(ExploreDbContext owner, List<Cerbos.Api.V1.Request.CheckResourcesRequest> requests,
            Effect effect, Func<Task> beforeReturn)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(async call =>
                {
                    if (owner.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                        throw new InvalidOperationException("Quota PDP selection must precede the original transaction.");
                    var request = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    requests.Add(request);
                    await beforeReturn();
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = request.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = request.Resources.Single().Resource.Kind, Id = request.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(request.Resources.Single().Actions.Single(), effect);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
