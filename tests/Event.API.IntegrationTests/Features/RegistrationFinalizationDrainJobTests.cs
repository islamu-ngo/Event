using Explore.API.Scheduling;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class RegistrationFinalizationDrainJobTests
{
    /// <summary>
    /// The command's lease owner, batch size, and lease seconds are the fenced-drain contract. Asserting
    /// them here is what proves the migration replaced the worker's timer and nothing else — a job that
    /// quietly changed the batch size would be a claim-semantics change wearing a scheduling change's
    /// clothes.
    /// </summary>
    [Test]
    public async Task ExecuteSendsTheSharedFencedDrainCommandFromAScopedSender()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<DrainRegistrationFinalizationEffectsCommand>(), Arg.Any<CancellationToken>())
            .Returns(2);
        var services = new ServiceCollection();
        services.AddScoped(_ => sender);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var job = new RegistrationFinalizationDrainJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RegistrationFinalizationDrainJob>.Instance);

        await job.Execute(CreateContext());

        await sender.Received(1).Send(
            Arg.Is<DrainRegistrationFinalizationEffectsCommand>(command =>
                command.LeaseOwner == "registration-finalization-drain-job" &&
                command.BatchSize == 100 && command.LeaseSeconds == 60),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The sequential guarantee the old <c>while</c> loop provided for free is now an explicit attribute;
    /// losing it would let a slow pass overlap the next one and contend for the same claims.
    /// </summary>
    [Test]
    public async Task TheDrainJobForbidsConcurrentExecution()
    {
        var attributes = typeof(RegistrationFinalizationDrainJob)
            .GetCustomAttributes(typeof(DisallowConcurrentExecutionAttribute), inherit: false);

        await Assert.That(attributes).IsNotEmpty();
    }

    private static IJobExecutionContext CreateContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        context.MergedJobDataMap.Returns([]);
        return context;
    }
}
