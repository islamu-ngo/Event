using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class ApiKeyManagementCommitAdmissionTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("stamp")]
    [Arguments("binding")]
    [Arguments("membership")]
    [Arguments("owner")]
    [Arguments("request")]
    [Arguments("expiry_after_reads")]
    public async Task TenantKeyPolicyRequiresOriginalAuthorityAfterActualUpdate(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        var command = new UpdateExternalApiKeyPolicyCommand
        {
            ExternalApiKeyId = keyId,
            ExternalApiKeyPolicyDto = new UpdateExternalApiKeyPolicyDto
            {
                Metadata = new() { Name = "Admitted policy" }
            }
        };
        bool observed = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            observed = await context.ExternalApiKeys.AsNoTracking()
                .AnyAsync(row => row.Id == keyId && row.Name == "Admitted policy", token);
            await Assert.That(observed).IsTrue();
            switch (change)
            {
                case "stamp":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "binding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("D")), token);
                    break;
                case "membership":
                    await context.TenantUsers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
                    break;
                case "owner":
                    await context.ExternalApiKeys.Where(row => row.Id == keyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.OwnerId, Guid.CreateVersion7()), token);
                    break;
                case "request":
                    request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                    break;
                case "expiry_after_reads":
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await context.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId, finalToken);
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            - fixture.Clock.GetUtcNow());
                    }, _ => { });
                    break;
            }
        });
        async Task InvokeAsync()
        {
            var result = await ActivatorUtilities.CreateInstance<UpdateExternalApiKeyPolicyCommandHandler>(services, owner)
                .ExecuteAsync(command, default);
            await Assert.That(result.IsSuccess).IsTrue();
        }
        if (change == "unchanged") await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var key = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId);
        await Assert.That(key.Name).IsEqualTo(change == "unchanged" ? "Admitted policy" : "Source custody");
        await Assert.That(key.OwnerId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(await observer.TenantUsers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.IsDeleted).SingleAsync()).IsFalse();
    }
}
