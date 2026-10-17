using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanEmailSourceProtocolTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticCredentialAndSelectedEmailOwnerProduceCompleteSources(bool tenant)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: false, tenantAdministrator: false,
            configureServices: services => services.AddHumanEmailSources(IdentityDatabaseTopology.Colocated));
        await fixture.ExecuteAsync(tenant, async services =>
        {
            var protocol = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            await Assert.That(ReferenceEquals(protocol.Subject, fixture.Subject)).IsTrue();
            await Assert.That(ReferenceEquals(protocol.Lifetime, fixture.Lifetime)).IsTrue();
            var prepared = await protocol.PrepareAsync(includeSelectedRoute: true);
            HumanPrincipalSourceValidation.RequireCurrent(protocol.Subject, protocol.Lifetime.LocalSession, prepared.Facts.Principal);
            // These are ordinary-human facts, not a SQL-login-derived administrator decision.
            await Assert.That(prepared.Facts.Principal.PlatformGrants.IsEmpty).IsTrue();
            await Assert.That(prepared.Facts.Principal.LocalSession!.Operations.IsEmpty).IsFalse();
            await Assert.That(prepared.Facts.Route).IsNotNull();
            var context = services.GetRequiredService<ExploreDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var acquired = await protocol.AcquireAsync(prepared);
            var fresh = await protocol.RequireOwnedAsync(acquired, fixture.Subject, fixture.Lifetime,
                tenant ? HumanEmailSourcePurpose.Tenant : HumanEmailSourcePurpose.Instance,
                tenant ? fixture.TenantId : null);
            HumanPrincipalSourceValidation.RequireCurrent(protocol.Subject, protocol.Lifetime.LocalSession, fresh.Principal);
            await Assert.That(ReferenceEquals(fresh.Support.Forwarding, protocol.Forwarding)).IsTrue();
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task CallerImplementedPreparationCannotMintAcquiredSourceProofOrRetry()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var protocol = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            var prepared = await protocol.PrepareAsync(false);
            await using var transaction = await services.GetRequiredService<ExploreDbContext>()
                .Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => protocol.AcquireAsync(new ForgedPreparation(prepared.Facts)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => protocol.AcquireAsync(prepared));
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments("caller")]
    [Arguments("producer")]
    [Arguments("purpose")]
    [Arguments("target")]
    public async Task ClosedDataPurposeRejectsCopiedOrForeignAcquiredFacts(string forgery)
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var factory = services.GetRequiredService<IHumanEmailSourceFactory>();
            var first = await factory.CreateAsync();
            var second = await factory.CreateAsync();
            var prepared = await first.PrepareAsync(false);
            var other = await second.PrepareAsync(false);
            await using var transaction = await services.GetRequiredService<ExploreDbContext>()
                .Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var acquired = await first.AcquireAsync(prepared);
            var foreign = await second.AcquireAsync(other);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.RequireOwnedAsync(
                forgery == "caller" ? new ForgedLease(acquired.Facts) : forgery == "producer" ? foreign : acquired,
                fixture.Subject, fixture.Lifetime,
                forgery == "purpose" ? HumanEmailSourcePurpose.Tenant : HumanEmailSourcePurpose.Instance,
                forgery == "target" ? fixture.TenantId : null));
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task OriginalTransactionCannotBeReplacedAfterRollback()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var protocol = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            var prepared = await protocol.PrepareAsync(false);
            var context = services.GetRequiredService<ExploreDbContext>();
            IHumanEmailSourceLease acquired;
            await using (var original = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                acquired = await protocol.AcquireAsync(prepared);
                await original.RollbackAsync();
            }
            await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => protocol.RequireOwnedAsync(acquired,
                fixture.Subject, fixture.Lifetime, HumanEmailSourcePurpose.Instance, null));
            await replacement.RollbackAsync();
        });
    }

    [Test]
    public async Task FinalSourceReentryObservesOwnCredentialRevocationInsteadOfCachedAcquisition()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var protocol = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            var prepared = await protocol.PrepareAsync(false);
            var context = services.GetRequiredService<ExploreDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var acquired = await protocol.AcquireAsync(prepared);
            HumanPrincipalSourceValidation.RequireCurrent(fixture.Subject, fixture.Lifetime.LocalSession, acquired.Facts.Principal);
            await context.LocalIdentityUsers.Where(row => row.Id == fixture.Subject.PrincipalId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("D")));
            var final = await protocol.RequireOwnedAsync(acquired, fixture.Subject, fixture.Lifetime,
                HumanEmailSourcePurpose.Instance, null);
            await Assert.That(final.Principal.LocalSession!.Accounts.Single().StampMatches).IsFalse();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            {
                HumanPrincipalSourceValidation.RequireCurrent(fixture.Subject, fixture.Lifetime.LocalSession, final.Principal);
                return Task.CompletedTask;
            });
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task IndependentLocalIdentityCannotBeDeclaredColocatedByTheSqlLogin()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            configureServices: services => services.AddHumanEmailSources(IdentityDatabaseTopology.External));
        await fixture.ExecuteAsync(false, async services =>
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync()));
    }

    [Test]
    public async Task ChangedOriginalHumanMarkerCannotKeepAnAcquiredSourceProof()
    {
        await using var fixture = await CreateAsync();
        await fixture.ExecuteAsync(false, async services =>
        {
            var protocol = await services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync();
            var prepared = await protocol.PrepareAsync(false);
            var context = services.GetRequiredService<ExploreDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var acquired = await protocol.AcquireAsync(prepared);
            context.HumanCommitAuditSubject = null;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => protocol.RequireOwnedAsync(acquired,
                    fixture.Subject, fixture.Lifetime, HumanEmailSourcePurpose.Instance, null));
            }
            finally
            {
                context.HumanCommitAuditSubject = fixture.Subject;
                await transaction.RollbackAsync();
            }
        });
    }

    private static Task<SmtpAuthenticatedFixture> CreateAsync() => SmtpAuthenticatedFixture.CreateAsync(
        configureServices: services => services.AddHumanEmailSources(IdentityDatabaseTopology.Colocated));

    private sealed record ForgedPreparation(HumanEmailSourceFacts Facts) : IHumanEmailSourcePreparation;
    private sealed record ForgedLease(HumanEmailSourceFacts Facts) : IHumanEmailSourceLease;
}
