using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private sealed record PreferenceTenant(Guid TenantId) : ITenantContext;

    [Test]
    public async Task TenantPreferenceCannotMutateAnotherAuthenticatedSubject()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var before = await db.Users.AsNoTracking().Where(row => row.Id == data.OwnerId)
            .Select(row => row.LastActiveTenantId).SingleAsync();
        var handler = ActivatorUtilities.CreateInstance<UpdateUserLastActiveTenantCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(
            new UpdateUserLastActiveTenantCommand(data.OwnerId, db.TenantContext!.TenantId), default);

        await Assert.That(result).IsFalse();
        await Assert.That(await db.Users.AsNoTracking().Where(row => row.Id == data.OwnerId)
            .Select(row => row.LastActiveTenantId).SingleAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task TenantRedirectionCannotEnumerateAnotherSubjectsMemberships()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var handler = ActivatorUtilities.CreateInstance<ResolveUserTenantRedirectionRequestHandler>(scope.ServiceProvider);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            handler.QueryAsync(new ResolveUserTenantRedirectionRequest(data.OwnerId), default));
    }

    [Test]
    public async Task SelfOwnedTenantSwitchUsesIndependentExactTargetAuthority()
    {
        using var jwt = new LocalizationNativeJwtAuthority();
        await using var factory = new NativeEventSeriesFactory { JwtAuthority = jwt };
        var data = await Seed(factory);
        var authenticated = await PreferenceScope(factory, jwt, data.OwnerId);
        using var scope = authenticated.Scope;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = authenticated.Request;
        var requestTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var originalTenant = requestTenant.TenantId;
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
        await using (var target = await contexts.CreateDbContextAsync())
        {
            target.TenantContext = new PreferenceTenant(data.ForeignTenantId);
            var actorId = await target.Actors.AsNoTracking().Where(actor => actor.UserId == data.OwnerId)
                .Select(actor => actor.Id).SingleAsync();
            target.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = data.ForeignTenantId,
                Tenant = null!,
                UserId = data.OwnerId,
                User = null!,
                ActorId = actorId,
                StatusId = (int)TenantUserStatusEnum.Active,
                CreatedAt = DateTime.UtcNow
            });
            await target.SaveChangesAsync();
        }
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateUserLastActiveTenantCommand, bool>>();

        var result = await handler.ExecuteAsync(
            new UpdateUserLastActiveTenantCommand(data.OwnerId, data.ForeignTenantId), default);

        await Assert.That(result).IsTrue();
        await Assert.That(requestTenant.TenantId).IsEqualTo(originalTenant);
        await using var verification = await contexts.CreateDbContextAsync();
        verification.TenantContext = new PreferenceTenant(originalTenant);
        await Assert.That(await verification.Users.AsNoTracking().Where(row => row.Id == data.OwnerId)
            .Select(row => row.LastActiveTenantId).SingleAsync()).IsEqualTo((Guid?)data.ForeignTenantId);
    }

    [Test]
    public async Task RevokedMembershipCannotSetTheLastActiveTenant()
    {
        using var jwt = new LocalizationNativeJwtAuthority();
        await using var factory = new NativeEventSeriesFactory { JwtAuthority = jwt };
        var data = await Seed(factory);
        var authenticated = await PreferenceScope(factory, jwt, data.OwnerId);
        using var scope = authenticated.Scope;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = authenticated.Request;
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var membership = await db.TenantUsers.SingleAsync(row => row.UserId == data.OwnerId);
        membership.StatusId = (int)TenantUserStatusEnum.Suspended;
        await db.SaveChangesAsync();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateUserLastActiveTenantCommand, bool>>();

        var result = await handler.ExecuteAsync(
            new UpdateUserLastActiveTenantCommand(data.OwnerId, membership.TenantId), default);

        await Assert.That(result).IsFalse();
    }

    private static async Task<(IServiceScope Scope, HttpContext Request)> PreferenceScope(
        NativeEventSeriesFactory factory, LocalizationNativeJwtAuthority jwt, Guid subject)
    {
        using (var arrangement = Scope(factory))
        {
            var db = arrangement.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var user = await db.Users.SingleAsync(row => row.Id == subject);
            db.UserExternalLogins.Add(jwt.Binding(user));
            await db.SaveChangesAsync();
        }
        var scope = Scope(factory);
        try
        {
            var request = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            request.RequestServices = scope.ServiceProvider;
            request.Request.Headers.Authorization = $"Bearer {jwt.Token(subject)}";
            var authentication = await request.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!authentication.Succeeded || authentication.Principal is null)
                throw new InvalidOperationException("The preference test requires validated Bearer authentication.");
            request.User = authentication.Principal;
            return (scope, request);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    [Test]
    public async Task DeletedSubjectCannotUseAnEarlierAuthenticatedPrincipalForRedirection()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var user = await db.Users.SingleAsync(row => row.Id == data.OwnerId);
        user.IsDeleted = true;
        await db.SaveChangesAsync();
        var handler = ActivatorUtilities.CreateInstance<ResolveUserTenantRedirectionRequestHandler>(scope.ServiceProvider);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            handler.QueryAsync(new ResolveUserTenantRedirectionRequest(data.OwnerId), default));
    }
}
