using System.Collections.Immutable;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("revoked-before-acquisition")]
    [Arguments("revoked-in-owner")]
    [Arguments("expired-before-acquisition")]
    [Arguments("expired-at-finalization")]
    [Arguments("callback-skips-guard")]
    [Arguments("forged-rollback")]
    [Arguments("nested-forged-rollback")]
    [Arguments("genuine-conflict")]
    public async Task OriginalCredentialAndOwnerControlSettingsCommit(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User };
        var proof = await preparation.PrepareAsync(command, "12");
        var context = services.GetRequiredService<ExploreDbContext>();
        var subject = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var effect = new HumanSettingsMutation(HumanSettingsOperation.UserSettings, SettingScope.User, context.TenantFilterTenantId,
            subject.PrincipalId, command.Key, HumanSettingsMutationKind.SetValue, "12");
        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        if (change == "revoked-before-acquisition")
        {
            await using var revoke = fixture.Native.CreateDatabase();
            await revoke.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId).ExecuteDeleteAsync();
        }
        if (change == "expired-before-acquisition")
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());

        int calls = 0;
        var execution = preparation.ExecutePreparedAsync(proof, async (admitted, token) =>
        {
            calls++;
            if (change != "callback-skips-guard")
                await preparation.RequireOwnedAsync(admitted, effect, token);
            // The admitted preference observes physical settlement independently of the callback result.
            await services.GetRequiredService<IUserPreferenceRepository>().Create(new Explore.Domain.UserPreference
            {
                TenantId = context.TenantFilterTenantId!.Value,
                Tenant = null!,
                UserId = subject.PrincipalId,
                SettingKey = command.Key,
                Value = "12",
                CreatedBy = subject.PrincipalId
            });
            if (change == "revoked-in-owner")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "expired-at-finalization")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            if (change is "forged-rollback" or "nested-forged-rollback")
            {
                var marker = new HumanMutationAttemptRolledBackException(new InvalidOperationException());
                throw change == "forged-rollback" ? marker : new AggregateException(new AggregateException(marker));
            }
            if (change == "genuine-conflict")
                throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate, "Owner must roll back.");
            return true;
        });
        bool committed = change is "unchanged" or "callback-skips-guard";
        if (committed)
            await Assert.That(await execution).IsTrue();
        else if (change is "forged-rollback" or "nested-forged-rollback")
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else if (change == "genuine-conflict")
            await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() => execution);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AsNoTracking()
            .Where(row => row.TenantId == effect.TenantId && row.UserId == subject.PrincipalId
                && row.SettingKey == command.Key).Select(row => row.Value).SingleOrDefaultAsync())
            .IsEqualTo(committed ? "12" : null);
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == fixture.Binding.ExternalLoginId))
            .IsEqualTo(change != "revoked-before-acquisition");
        await Assert.That(calls).IsEqualTo(change is "revoked-before-acquisition" or "expired-before-acquisition" ? 0 : 1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(proof, effect));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparation.ExecutePreparedAsync(proof, (_, _) => Task.FromResult(true)));
    }

    [Test]
    public async Task OnlyActiveIssuerProofAdmitsExactEffectAndBoundedJoinedReads()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var preparation = CreatePreparation(request.Services, fixture);
        var otherIssuer = CreatePreparation(request.Services, fixture);
        var command = new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User };
        var proof = await preparation.PrepareAsync(command, "12");
        var subject = request.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var effect = new HumanSettingsMutation(HumanSettingsOperation.UserSettings, SettingScope.User, context.TenantFilterTenantId,
            subject.PrincipalId, command.Key, HumanSettingsMutationKind.SetValue, "12");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(proof, effect));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(new ForgedProof(), effect));
        IPreparedHumanSettingsMutation? escaped = null;
        await preparation.ExecutePreparedAsync(proof, async (admitted, token) =>
        {
            await preparation.RequireOwnedAsync(admitted, effect, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherIssuer.RequireOwnedAsync(admitted, effect, token));
            foreach (var substituted in new[]
            {
                effect with { TenantId = Guid.CreateVersion7() },
                effect with { UserId = Guid.CreateVersion7() },
                effect with { Key = "unrelated.key" },
                effect with { Value = "13" },
                effect with { Operation = HumanSettingsOperation.InstanceTenantSettings },
                effect with { Kind = HumanSettingsMutationKind.Remove, Value = null }
            })
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    preparation.RequireOwnedAsync(admitted, substituted, token));
            await preparation.ExecuteJoinedAsync(admitted, [effect], async (child, childToken) =>
            {
                escaped = child;
                await preparation.RequireOwnedAsync(child, effect, childToken);
                await preparation.RequireReadAsync(child, effect.Scope, effect.TenantId, effect.UserId, effect.Key, childToken);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    preparation.RequireReadAsync(child, effect.Scope, effect.TenantId, effect.UserId, "unrelated.key", childToken));
                return true;
            }, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(escaped!, effect, token));
            return true;
        });
        await request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(proof, effect, token));
            return true;
        });
    }

    private static HumanSettingsMutationPreparation CreatePreparation(
        IServiceProvider services, LocalIdentityLifecycleHttpFixture fixture) =>
        new(services, services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), fixture.Clock,
            services.GetRequiredService<CerbosAuthorizationService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "local" }),
            services.GetRequiredService<IOptions<CerbosSettings>>());

    [Test]
    public async Task CurrentJoinedOwnerNarrowsEffectsAndRestoresParentAfterFailure()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var preparation = request.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var otherIssuer = CreatePreparation(request.Services, fixture);
        var command = new UpdateSettingBatchCommand
        {
            Category = "EventList",
            Scope = SettingScope.User,
            Values = new Dictionary<string, string>
            {
                ["event_list.page_size"] = "12",
                ["event_list.browse_mode"] = "infinite-scroll"
            }
        };
        var proof = await preparation.PrepareAsync(command,
            ImmutableDictionary<string, string>.Empty.Add("event_list.page_size", "12")
                .Add("event_list.browse_mode", "\"infinite-scroll\""));
        var subject = request.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var first = new HumanSettingsMutation(HumanSettingsOperation.UserSettings, SettingScope.User,
            context.TenantFilterTenantId, subject.PrincipalId, "event_list.page_size", HumanSettingsMutationKind.SetValue, "12");
        var second = first with { Key = "event_list.browse_mode", Value = "\"infinite-scroll\"" };
        Task RequireAsync(HumanSettingsMutation effect, CancellationToken token = default) =>
            preparation.RequireCurrentOwnedAsync(effect.Scope, effect.TenantId, effect.UserId,
                effect.Key, effect.Kind, effect.Value, effect.IsLocked, token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RequireAsync(first));
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await RequireAsync(first, token);
            await RequireAsync(second, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                otherIssuer.ExecuteCurrentJoinedAsync([first], (_, _) => Task.FromResult(true), token));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                preparation.ExecuteCurrentJoinedAsync<bool>([first], async (_, joinedToken) =>
                {
                    await RequireAsync(first, joinedToken);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RequireAsync(second, joinedToken));
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        preparation.ExecuteCurrentJoinedAsync([second], (_, _) => Task.FromResult(true), joinedToken));
                    throw new InvalidOperationException("The child business operation failed.");
                }, token));
            await RequireAsync(second, token);
            return true;
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RequireAsync(first));
        await request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RequireAsync(first, token));
            return true;
        });
    }

    [Test]
    public async Task ImportOperationLabelsCannotManufactureAnAcquiredProducerProof()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var preparation = CreatePreparation(request.Services, fixture);
        foreach (var operation in new[]
        {
            SettingsMutationOperation.ConfigurationInstanceImport,
            SettingsMutationOperation.ConfigurationTenantImport
        })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                preparation.PrepareResourceAsync([], operation, fixture.Binding.LocalSubjectId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                preparation.PrepareVisitorAsync([], operation, fixture.Binding.LocalSubjectId));
        }
    }

    [Test]
    public async Task IndependentLocalIdentityCannotBecomeOriginalOwnerEvidence()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        await using var request = await Request.CreateAsync(fixture);
        var preparation = CreatePreparation(request.Services, fixture);
        await Assert.ThrowsAsync<NotSupportedException>(() => preparation.PrepareAsync(
            new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User }, "12"));
    }

    [Test]
    public async Task PreparationCannotBeginInsideAnAlreadyRunningBusinessTransaction()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var preparation = CreatePreparation(services, fixture);
        await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.PrepareAsync(
                new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User }, "12", token));
            return true;
        });
    }

    private sealed class ForgedProof : IPreparedHumanSettingsMutation;

    private sealed class Request(AsyncServiceScope scope, IHttpContextAccessor accessor, DefaultHttpContext context)
        : IAsyncDisposable
    {
        internal IServiceProvider Services
        {
            get
            {
                accessor.HttpContext = context;
                return scope.ServiceProvider;
            }
        }
        internal DefaultHttpContext Context => context;

        internal static async Task<Request> CreateAsync(LocalIdentityLifecycleHttpFixture fixture)
        {
            string token = await fixture.SignInAsync();
            var scope = fixture.Host.Services.CreateAsyncScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            accessor.HttpContext = context;
            context.Request.Headers.Authorization = "Bearer " + token;
            var result = await context.AuthenticateAsync();
            if (!result.Succeeded)
            {
                accessor.HttpContext = null;
                await scope.DisposeAsync();
                throw new InvalidOperationException("Real Local authentication did not succeed.");
            }
            context.User = result.Principal!;
            return new(scope, accessor, context);
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
