using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

/// <summary>Actual API-authenticated persistence registrations must retain the selected writer owner.</summary>
[NotInParallel]
public sealed class IdentityWriterSourceRegistrationTests
{
    [Test]
    public async Task ProductionPortsUseTheSelectedOriginalSourceSetAndUncommittedBinding()
    {
        await using var fixture = await AuthenticatedSettingsFixture.CreateSqliteAsync();
        await fixture.RunAsync(null, async session =>
        {
            var context = session.Context;
            BindSelected(session);
            await using var gate = new HumanSecuritySourceGate(context);
            await using var held = await gate.AcquireAsync();
            await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(context, default);
            var principal = session.Services.GetRequiredService<IHumanPrincipalSourceFence>();
            var route = session.Services.GetRequiredService<IHumanAuthorizationRouteSource>();
            var sourceSet = session.Services.GetRequiredService<IPolicySourceSetFence>();
            var bindings = session.Services.GetRequiredService<IExternalLoginAuthenticationRepository>();
            var connector = context.Database.GetDbConnection();
            var observed = await principal.ObserveAsync();
            await Assert.That(observed.Users.Single().Id).IsEqualTo(session.ActorId);
            await Assert.That(observed.LocalSession!.Accounts.Single().StampMatches).IsTrue();
            await Assert.That((await route.ObserveAsync()).Settings.Select(row => row.Key)
                .SequenceEqual(HumanAuthorizationRouteFacts.Keys)).IsTrue();
            var binding = NewBinding(session.ActorId);
            var key = new ProviderAccountKey(AuthenticationProviderKind.Local, binding.ProviderKey);
            await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var issued = await sourceSet.AcquireAsync();
                var principalLease = await principal.AcquireAsync(issued);
                var routeLease = await route.AcquireAsync(issued);
                context.UserExternalLogins.Add(binding);
                await context.SaveChangesAsync();

                var ownBinding = await bindings.GetByProviderAndKey(key);

                await Assert.That(ownBinding).IsNotNull();
                await Assert.That(ownBinding!.Id).IsEqualTo(binding.Id);
                await Assert.That(ReferenceEquals(ownBinding, binding)).IsFalse();
                await principalLease.ValidateAsync();
                await routeLease.ValidateAsync();
                await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
                gate.RequireCurrent();
                await transaction.RollbackAsync();
            }
            await Assert.That(await bindings.GetByProviderAndKey(key)).IsNull();
            return 0;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProductionSelectedSourcePortsRejectCallerForgedSourceSetLeases(bool routePort)
    {
        await using var fixture = await AuthenticatedSettingsFixture.CreateSqliteAsync();
        await fixture.RunAsync(null, async session =>
        {
            BindSelected(session);
            await using var gate = new HumanSecuritySourceGate(session.Context);
            await using var held = await gate.AcquireAsync();
            await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(session.Context, default);
            var principal = session.Services.GetRequiredService<IHumanPrincipalSourceFence>();
            var route = session.Services.GetRequiredService<IHumanAuthorizationRouteSource>();
            await using var transaction = await session.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var forged = new ForgedSourceSetLease();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                if (routePort)
                    await route.AcquireAsync(forged);
                else
                    await principal.AcquireAsync(forged);
            });

            await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
            return 0;
        });
    }

    [Test]
    public async Task StaleSelectedOwnerCannotFallBackToAnIndependentGlobalBindingReader()
    {
        await using var fixture = await AuthenticatedSettingsFixture.CreateSqliteAsync();
        await fixture.RunAsync(null, async session =>
        {
            var context = session.Context;
            BindSelected(session);
            var key = new ProviderAccountKey(AuthenticationProviderKind.Local, session.ActorId.ToString("D"));
            await Assert.That(await context.UserExternalLogins.AsNoTracking().AnyAsync(row =>
                row.AuthenticationProviderId == (int)key.ProviderKind && row.ProviderKey == key.Value)).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await context.DisposeAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await session.Services.GetRequiredService<IExternalLoginAuthenticationRepository>().GetByProviderAndKey(key));
            return 0;
        });
    }

    [Test]
    public async Task OrdinaryUnselectedScopeKeepsGlobalCommittedBindingIsolationEvenWithASecurityGate()
    {
        await using var fixture = await AuthenticatedSettingsFixture.CreateSqliteAsync();
        await fixture.RunAsync(null, async session =>
        {
            var selected = session.Services.GetRequiredService<LocalIdentityExecutionLease>();
            await Assert.That(selected.Application).IsNull();
            await Assert.That(selected.Identity).IsNull();
            await using var gate = new HumanSecuritySourceGate(session.Context);
            await using var held = await gate.AcquireAsync();
            var repository = session.Services.GetRequiredService<IExternalLoginAuthenticationRepository>();
            var binding = NewBinding(session.ActorId);
            var key = new ProviderAccountKey(AuthenticationProviderKind.Local, binding.ProviderKey);
            await using var transaction = await session.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            session.Context.UserExternalLogins.Add(binding);
            await session.Context.SaveChangesAsync();

            await Assert.That(await repository.GetByProviderAndKey(key)).IsNull();
            await Assert.That(await session.Context.UserExternalLogins.AsNoTracking()
                .AnyAsync(row => row.Id == binding.Id)).IsTrue();
            await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
            return 0;
        });
    }

    private static void BindSelected(AuthenticatedSettingsFixture.Session session)
    {
        var ordinary = session.Services.GetRequiredService<ExploreDbContextExecutionLease>();
        ordinary.RequireOriginalOwner(session.Context);
        if (ordinary.HumanScope?.Subject.PrincipalId != session.ActorId || ordinary.HumanLifetime?.LocalSession is null)
            throw new InvalidOperationException("The actual API-authenticated original human lifetime is required.");
        var selected = session.Services.GetRequiredService<LocalIdentityExecutionLease>();
        selected.Bind(session.Context, session.Context);
        selected.RequireCurrent(session.Context, session.Context);
    }

    private static UserExternalLogin NewBinding(Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
        AuthenticationProvider = null!,
        ProviderKey = Guid.CreateVersion7().ToString("D"),
        ProviderDisplayName = "Original writer registration probe",
        CreatedAt = DateTime.UtcNow
    };

    private sealed class ForgedSourceSetLease : IPolicySourceSetLease
    {
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
