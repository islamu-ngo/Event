using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class PdsSyncDrainServiceTests
{
    [Test]
    public async Task ProcessBatchAsync_ClaimsOneConfiguredBoundedBatch()
    {
        var repository = Substitute.For<IPdsSyncOutboxRepository>();
        repository.ClaimDueAsync(
                20,
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                TimeSpan.FromSeconds(90),
                Arg.Any<CancellationToken>())
            .Returns([]);
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var service = new PdsSyncDrainService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PdsSyncSettings()),
            TimeProvider.System);

        PdsSyncDrainResult result = await service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.ClaimedCount).IsEqualTo(0);
        await repository.Received(1).ClaimDueAsync(
            20,
            Arg.Is<string>(owner => owner.StartsWith("pds-", StringComparison.Ordinal) && owner.Length <= 200),
            Arg.Any<DateTime>(),
            TimeSpan.FromSeconds(90),
            Arg.Any<CancellationToken>());
    }
}
