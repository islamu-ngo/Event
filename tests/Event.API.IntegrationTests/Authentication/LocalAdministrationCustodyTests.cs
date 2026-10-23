using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class LocalAdministrationCustodyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateRetainsOriginalCredentialThroughTheIdentityCommit(bool expire)
    {
        Guid operationId = Guid.CreateVersion7();
        var barrier = new SourceReadBarrier(operationId);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => barrier.Decorate(services));
        await GrantAdministrator(fixture);
        string token = await fixture.SignInAsync();
        var body = new CreateLocalIdentityRequestDto
        {
            OperationId = operationId,
            Email = $"{Guid.CreateVersion7():N}@custody.test",
            FirstName = "Local",
            LastName = "Custody"
        };
        Task<HttpResponseMessage> pending = fixture.PostAuthorizedAsync("/api/instance/local-identities", body, token);
        try
        {
            await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            if (!barrier.Read.Task.IsCompleted) await pending;
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (expire) fixture.Clock.Advance(TimeSpan.FromDays(2));
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await pending;
        await using var identity = fixture.Native.CreateIdentityDatabase();
        bool stored = await identity.Set<Explore.Persistence.Identity.LocalIdentityCredentialOperation>()
            .AsNoTracking().AnyAsync(row => row.Id == operationId);
        await Assert.That(stored).IsEqualTo(!expire);
        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(!expire);
        if (!expire) await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .AnyAsync(row => row.UserId != fixture.Binding.LocalSubjectId
                && row.User!.Pii!.Email == body.Email)).IsEqualTo(!expire);
    }

    [Test]
    [Arguments("allowed")]
    [Arguments("expired")]
    [Arguments("stamp")]
    [Arguments("hash")]
    public async Task SelfResetAllowsOnlyItsOwnClosedCredentialTransition(string change)
    {
        Guid operationId = Guid.CreateVersion7();
        var barrier = new SourceReadBarrier(operationId);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => barrier.Decorate(services));
        await GrantAdministrator(fixture);
        string token = await fixture.SignInAsync();
        var before = await fixture.ReadIdentityAsync();
        if (change is "stamp" or "hash")
            barrier.BeforeRead = async (context, cancellationToken) =>
            {
                if (change == "stamp")
                    await context.Set<Explore.Persistence.Identity.LocalIdentityUser>()
                        .Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), cancellationToken);
                else
                    await context.Set<Explore.Persistence.Identity.LocalIdentityUser>()
                        .Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.PasswordHash,
                            before.PasswordHash), cancellationToken);
            };
        LocalCredentialOperationStatus current;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
            var page = await service.ListAsync(new(1, 100), default);
            Guid currentId = page.Items.Single(row => row.LocalSubjectId == fixture.Binding.LocalSubjectId)
                .CurrentOperationId!.Value;
            current = (await service.ReadOperationAsync(currentId, default))!;
        }
        Task<HttpResponseMessage> pending = fixture.PostAuthorizedAsync(
            $"/api/instance/local-identities/{fixture.Binding.LocalSubjectId}/temporary-credential",
            new ResetLocalCredentialRequestDto
            {
                OperationId = operationId,
                ExpectedCurrentOperationId = current.Receipt.OperationId,
                ExpectedCurrentOperationConcurrencyStamp = current.OperationConcurrencyStamp,
                Reason = "Original owner custody"
            }, token);
        try
        {
            await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            if (!barrier.Read.Task.IsCompleted) await pending;
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (change == "expired") fixture.Clock.Advance(TimeSpan.FromDays(2));
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await pending;
        bool accepted = change == "allowed";
        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(accepted);
        var after = await fixture.ReadIdentityAsync();
        await Assert.That(after.SecurityStamp == before.SecurityStamp).IsEqualTo(!accepted);
        await Assert.That(after.PasswordHash == before.PasswordHash).IsEqualTo(!accepted);
        await using var identity = fixture.Native.CreateIdentityDatabase();
        await Assert.That(await identity.Set<Explore.Persistence.Identity.LocalIdentityCredentialOperation>()
            .AsNoTracking().AnyAsync(row => row.Id == operationId)).IsEqualTo(accepted);
    }

    private static async Task GrantAdministrator(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        database.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await database.SaveChangesAsync();
    }

    // This observes the real source lease after its actual read. It returns unchanged
    // facts and never supplies authority, a policy answer, or a replacement owner.
    private sealed class SourceReadBarrier(Guid operationId)
    {
        private Guid OperationId { get; } = operationId;
        internal TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<ExploreDbContext, CancellationToken, Task>? BeforeRead { get; set; }
        private int entered;

        internal void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IHumanPrincipalSourceFence));
            services.Remove(descriptor);
            services.AddScoped<IHumanPrincipalSourceFence>(provider => new Source(
                (IHumanPrincipalSourceFence)descriptor.ImplementationFactory!(provider),
                provider.GetRequiredService<ExploreDbContext>(), this));
        }

        private sealed class Source(IHumanPrincipalSourceFence inner, ExploreDbContext context,
            SourceReadBarrier barrier) : IHumanPrincipalSourceFence
        {
            public Task<HumanPrincipalSourceFacts> ObserveAsync(CancellationToken cancellationToken = default) =>
                inner.ObserveAsync(cancellationToken);
            public async Task<IHumanPrincipalSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
                CancellationToken cancellationToken = default) =>
                new Lease(await inner.AcquireAsync(sourceSet, cancellationToken), context, barrier);
        }

        private sealed class Lease(IHumanPrincipalSourceLease inner, ExploreDbContext context,
            SourceReadBarrier barrier) : IHumanPrincipalSourceLease
        {
            public HumanPrincipalSourceFacts Facts => inner.Facts;
            public async Task<HumanPrincipalSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
            {
                bool selected = await context.Set<Explore.Persistence.Identity.LocalIdentityCredentialOperation>()
                    .AsNoTracking().AnyAsync(row => row.Id == barrier.OperationId, cancellationToken)
                    && Interlocked.Exchange(ref barrier.entered, 1) == 0;
                if (selected && barrier.BeforeRead is not null) await barrier.BeforeRead(context, cancellationToken);
                var facts = await inner.ValidateAsync(cancellationToken);
                if (selected)
                {
                    barrier.Read.TrySetResult();
                    await barrier.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
                return facts;
            }
        }
    }
}
