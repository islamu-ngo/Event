using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

/// <summary>Actual source ports must consume the selected pair and EF projections, not the old SQL reader.</summary>
[NotInParallel]
public sealed class IdentityWriterHumanSourceOwnerTests
{
    [Test]
    public async Task SelectedPrincipalAndRoutePortsReadThroughEfOnTheOriginalPreBeginOwner()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, local) = await SeedLocalSubjectAsync(fixture);
        var reads = new LinqReadObserver();
        await using var context = CreateOwner(fixture, reads);
        var selected = Bind(context);
        await using var gate = new HumanSecuritySourceGate(context);
        await using var held = await gate.AcquireAsync();
        await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(context, default);
        var connector = context.Database.GetDbConnection();
        var principal = new HumanPrincipalSourceFence(selected, subject, local);
        var route = new HumanAuthorizationRouteSource(selected, subject);
        reads.Reset();

        var principalFacts = await principal.ObserveAsync();

        await Assert.That(principalFacts.Users.Single().Id).IsEqualTo(fixture.UserId);
        await Assert.That(principalFacts.LocalSession!.Accounts.Single().StampMatches).IsTrue();
        await Assert.That(reads.LinqReads > 0).IsTrue();
        reads.Reset();
        var routeFacts = await route.ObserveAsync();
        await Assert.That(routeFacts.Settings.Select(row => row.Key).SequenceEqual(HumanAuthorizationRouteFacts.Keys)).IsTrue();
        await Assert.That(reads.LinqReads > 0).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
        gate.RequireCurrent();
    }

    [Test]
    public async Task BothSourceLeasesObserveOwnWritesButCannotOutliveTheirOriginalSerializableTransaction()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, local) = await SeedLocalSubjectAsync(fixture);
        await using var context = CreateOwner(fixture);
        var selected = Bind(context);
        await using var gate = new HumanSecuritySourceGate(context);
        await using var held = await gate.AcquireAsync();
        await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(context, default);
        var principal = new HumanPrincipalSourceFence(selected, subject, local);
        var route = new HumanAuthorizationRouteSource(selected, subject);
        var sourceSet = new PolicySourceSetFence(selected);
        IHumanPrincipalSourceLease principalLease;
        IHumanAuthorizationRouteLease routeLease;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var policyLease = await sourceSet.AcquireAsync();
            principalLease = await principal.AcquireAsync(policyLease);
            routeLease = await route.AcquireAsync(policyLease);
            await Assert.That(principalLease.Facts.Users.Single().IsDeleted).IsFalse();
            await context.Users.Where(row => row.Id == fixture.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            var current = await principalLease.ValidateAsync();
            await Assert.That(current.Users.Single().IsDeleted).IsTrue();
            await Assert.That(principalLease.Facts.Users.Single().IsDeleted).IsFalse();
            await routeLease.ValidateAsync();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => principalLease.ValidateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => routeLease.ValidateAsync());
        await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => principalLease.ValidateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => routeLease.ValidateAsync());
        await replacement.RollbackAsync();
        await Assert.That((await principal.ObserveAsync()).Users.Single().IsDeleted).IsFalse();
    }

    [Test]
    public async Task SelectedPortsRejectMissingSecurityGateBeforeAnySourceRead()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, local) = await SeedLocalSubjectAsync(fixture);
        var reads = new LinqReadObserver();
        await using var context = CreateOwner(fixture, reads);
        var selected = Bind(context);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new HumanPrincipalSourceFence(selected, subject, local).ObserveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new HumanAuthorizationRouteSource(selected, subject).ObserveAsync());
        await Assert.That(reads.LinqReads).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ACallerImplementedPolicyLeaseCannotReplaceTheSelectedOwnersIssuedSourceSet()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, local) = await SeedLocalSubjectAsync(fixture);
        await using var context = CreateOwner(fixture);
        var selected = Bind(context);
        await using var gate = new HumanSecuritySourceGate(context);
        await using var held = await gate.AcquireAsync();
        await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(context, default);
        var principal = new HumanPrincipalSourceFence(selected, subject, local);
        var route = new HumanAuthorizationRouteSource(selected, subject);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var forgery = new ForgedPolicyLease();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => principal.AcquireAsync(forgery));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => route.AcquireAsync(forgery));
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    private static ExploreDbContext CreateOwner(EventVisitorCapabilitySqliteFixture fixture, params IInterceptor[] interceptors)
    {
        var context = EmailDispatchSqliteFixture.CreateContext(fixture.DatabasePath, interceptors);
        context.TenantContext = fixture;
        context.BindTenantLease();
        return context;
    }

    private static LocalIdentityExecutionLease Bind(ExploreDbContext context)
    {
        var selected = new LocalIdentityExecutionLease();
        selected.Bind(context, context);
        return selected;
    }

    private static async Task<(HumanCommitSubject Subject, LocalSessionAuthority Local)> SeedLocalSubjectAsync(
        EventVisitorCapabilitySqliteFixture fixture)
    {
        string stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        fixture.Context.AddRange(new LocalIdentityUser
        {
            Id = fixture.UserId,
            FirstName = "Selected",
            LastName = "Source owner",
            PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            SecurityStamp = stamp,
            EmailConfirmed = true
        }, new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = fixture.UserId.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", fixture.UserId.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "selected-source-owner"));
        var subject = await HumanCommitSubject.ResolveAsync(principal,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
            ?? throw new InvalidOperationException("A persisted Local subject is required.");
        return (subject, new LocalSessionAuthority(fixture.UserId, stamp, true));
    }

    private sealed class ForgedPolicyLease : IPolicySourceSetLease
    {
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LinqReadObserver : DbCommandInterceptor
    {
        internal int LinqReads { get; private set; }
        internal void Reset() => LinqReads = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.CommandSource == CommandSource.LinqQuery)
                LinqReads++;
            return ValueTask.FromResult(result);
        }
    }
}
