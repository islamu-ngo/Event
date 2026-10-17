using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence.Identity;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class PlatformApiKeyOriginalOwnerTests
{
    [Test]
    [Arguments(0, "allow")]
    [Arguments(1, "allow")]
    [Arguments(2, "allow")]
    [Arguments(3, "allow")]
    [Arguments(0, "deny")]
    [Arguments(1, "stamp")]
    [Arguments(2, "grant")]
    [Arguments(3, "route")]
    public async Task OriginalInstanceQuestionPrecedesOwnerAndCannotSurviveSourceDrift(int operation, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""));
            seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = keyId,
                ExternalApiKey = null!,
                PeriodStart = new(2026, 1, 1),
                PeriodEnd = new(2026, 1, 31),
                RequestCount = 7,
                CreditsUsed = 2
            });
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var sent = ConfigurePolicy(request, async () =>
        {
            // A separate physical writer succeeds only while the dedicated owner is idle.
            await using var writer = fixture.Native.CreateDatabase();
            if (change == "stamp")
                await writer.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")));
            else if (change == "grant")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
            else if (change == "route")
                await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"local\""));
            else
                await writer.ExternalApiKeys.Where(row => row.Id == keyId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "Policy ran before transaction"));
        }, change == "deny" ? Effect.Deny : Effect.Allow);
        var operations = ActivatorUtilities.CreateInstance<InstanceApiKeyOperations>(request.Services, request.Factory);
        async Task InvokeAsync()
        {
            switch (operation)
            {
                case 0:
                    await Assert.That((await operations.ReadAsync(fixture.Binding.LocalSubjectId, keyId, default)).Single().Id).IsEqualTo(keyId);
                    break;
                case 1:
                    await Assert.That((await operations.UpdateAsync(fixture.Binding.LocalSubjectId, keyId,
                        new() { Metadata = new() { Name = "Admitted policy" } }, default)).IsSuccess).IsTrue();
                    break;
                case 2:
                    await Assert.That(await operations.RevokeAsync(fixture.Binding.LocalSubjectId, keyId, default)).IsTrue();
                    break;
                case 3:
                    await Assert.That((await operations.ReportAsync(fixture.Binding.LocalSubjectId,
                        new(2026, 1, 1), new(2026, 1, 31), default)).Single().TotalRequestCount).IsEqualTo(7L);
                    break;
            }
        }
        if (change == "allow") await InvokeAsync();
        else if (change == "deny") await Assert.ThrowsAsync<AuthorizationException>(InvokeAsync);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        var resource = sent.Single().Resources.Single();
        await Assert.That(resource.Resource.Kind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(resource.Resource.Id).IsEqualTo(operation == 3
            ? "instance.api-keys.usage-report" : $"instance.api-keys.{keyId:N}");
        await Assert.That(resource.Actions.Single()).IsEqualTo(operation switch
        {
            1 => AuthorizationActions.Update,
            2 => AuthorizationActions.Delete,
            _ => AuthorizationActions.View
        });
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId);
        await Assert.That(persisted.Name).IsEqualTo(change == "allow" && operation == 1 ? "Admitted policy" : "Original platform owner");
        await Assert.That(persisted.ExternalApiKeyStatusId).IsEqualTo(change == "allow" && operation == 2
            ? (int)ExternalApiKeyStatusEnum.Revoked : (int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    [Arguments("active")]
    [Arguments("extend_then_expire")]
    [Arguments("revoke")]
    public async Task DedicatedOwnerRetainsTheOriginalSupportHorizon(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else row.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Platform key administration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            sessionId = session.Id;
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        ConfigurePolicy(request, async () =>
        {
            await using var writer = fixture.Native.CreateDatabase();
            if (change == "extend_then_expire")
                await writer.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddMinutes(5)));
            if (change == "revoke")
                await writer.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)SupportAccessSessionStatusEnum.Revoked));
        }, Effect.Allow, forceCerbos: true);
        await using var scope = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, keyId, default);
        bool body = false;
        async Task InvokeAsync() => await scope.ExecuteAsync(InstanceApiKeyAction.Revoke, async token =>
        {
            body = true;
            bool result = await scope.RevokeAsync(fixture.Clock.GetUtcNow().UtcDateTime, token);
            if (change == "extend_then_expire") fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
            return result;
        }, default);
        if (change == "active") await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(body).IsEqualTo(change != "revoke");
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == keyId)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo(change == "active"
                ? (int)ExternalApiKeyStatusEnum.Revoked : (int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    public async Task IndependentLocalIdentityCannotBeRelabelledAsDedicatedSourceAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture) { IdentityTopology = IdentityDatabaseTopology.External };
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await request.Factory.CreateReadAsync(fixture.Binding.LocalSubjectId, key, default));
    }

    private static List<Cerbos.Api.V1.Request.CheckResourcesRequest> ConfigurePolicy(Request request,
        Func<Task> beforeReturn, Effect effect, bool forceCerbos = false, bool unavailable = false)
    {
        var sent = new List<Cerbos.Api.V1.Request.CheckResourcesRequest>();
        var client = Substitute.For<ICerbosClient>();
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);
        client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(async call =>
            {
                var check = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                sent.Add(check);
                if (System.Transactions.Transaction.Current is not null)
                    throw new InvalidOperationException("The selected platform PDP cannot execute inside a transaction.");
                await beforeReturn();
                if (unavailable) throw new RpcException(new Status(StatusCode.Unavailable, "External policy unavailable"));
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = check.RequestId };
                var resource = check.Resources.Single();
                var entry = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Kind = resource.Resource.Kind, Id = resource.Resource.Id }
                };
                entry.Actions.Add(resource.Actions.Single(), effect);
                response.Results.Add(entry);
                return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
            });
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://platform-key-pdp.invalid:3593" });
        request.Policy = new PlatformApiKeyPolicyPreparation(
            ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(request.Services, client, factory, options),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = forceCerbos ? "cerbos" : null }), options,
            request.Services.GetRequiredService<ITenantContext>(), request.Services.GetRequiredService<TimeProvider>());
        return sent;
    }
}
