using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class PlatformApiKeyOriginalOwnerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicReportSamplesCredentialTimeAfterTheFinalCompleteSourceRead(bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        DateOnly from = new(2026, 1, 1);
        DateOnly to = new(2026, 1, 31);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = key,
                ExternalApiKey = null!,
                PeriodStart = from,
                PeriodEnd = to,
                RequestCount = 11,
                CreditsUsed = 4
            });
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, default))!;
        bool finalReadObserved = false;
        request.Policy = new AfterFinalSourceRead(
            (IPlatformApiKeyPolicyPreparation)request.GetService(typeof(IPlatformApiKeyPolicyPreparation))!, () =>
            {
                finalReadObserved = true;
                if (expire) fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
            });
        await using var report = await request.Factory.CreateReportAsync(fixture.Binding.LocalSubjectId, default);
        if (expire)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => report.ReadAsync(from, to, default));
        else
            await Assert.That((await report.ReadAsync(from, to, default)).Single().TotalRequestCount).IsEqualTo(11L);
        await Assert.That(finalReadObserved).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => report.ReadAsync(from, to, default));
    }

    [Test]
    public async Task UnauthenticatedLowerFactoryCannotAcquirePlatformReportAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture);
        request.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = request.Context;
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
            await request.Factory.CreateReportAsync(fixture.Binding.LocalSubjectId, default));
    }

    /// <summary>
    /// Executes every genuine policy check unchanged. Only advances the real fixture clock at
    /// the terminal source-read event, after the owner's earlier acquisition check has finished.
    /// No source facts, authentication observations, owner or allow decision are substituted.
    /// </summary>
    private sealed class AfterFinalSourceRead(IPlatformApiKeyPolicyPreparation inner, Action afterFinalRead)
        : IPlatformApiKeyPolicyPreparation
    {
        public async Task<IPreparedPlatformApiKeyPolicy> PrepareAsync(InstanceApiKeyAction action, Guid? keyId,
            HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, PlatformApiKeySourceFacts original,
            CancellationToken cancellationToken) =>
            new Observed(await inner.PrepareAsync(action, keyId, subject, lifetime, original, cancellationToken), afterFinalRead);

        private sealed class Observed(IPreparedPlatformApiKeyPolicy inner, Action afterFinalRead) : IPreparedPlatformApiKeyPolicy
        {
            private bool acquired;
            public void ValidateSources(PlatformApiKeySourceFacts current)
            {
                inner.ValidateSources(current);
                if (acquired) afterFinalRead();
                acquired = true;
            }
            public void ValidateTime(PlatformApiKeySourceFacts current, DateTimeOffset now) => inner.ValidateTime(current, now);
        }
    }
}
