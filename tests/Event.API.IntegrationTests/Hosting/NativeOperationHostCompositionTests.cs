using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hosting;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Hosting;

public sealed class NativeOperationHostCompositionTests
{
    [Test]
    public async Task ActualApiProviderDeeplyResolvesEveryNativeHandlerInADisposableScope()
    {
        await using var factory = new NativeFactory();
        using var client = factory.CreateClient();
        using var alive = await client.GetAsync("/alive");
        alive.EnsureSuccessStatusCode();
        var evidence = factory.Services.GetRequiredService<ConstructionEvidence>();
        await Assert.That(evidence.Constructed).IsEqualTo(0);
        await factory.Services.ValidateNativeOperationsDeepAsync();
        await Assert.That(evidence.Constructed).IsEqualTo(1);
        await Assert.That(evidence.Disposed).IsEqualTo(1);
        await Assert.That(evidence.Executed).IsEqualTo(0);
    }

    [Test]
    public async Task FinalHostServicesRejectLateRawHandlerAfterAllModulesAndTestOverrides()
    {
        await using var factory = new NativeFactory(addLateRawHandler: true);
        await Assert.That(() => factory.CreateClient()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BootMetadataDoesNotConstructTheOperationGraph(bool openApi)
    {
        var builder = WebApplication.CreateBuilder();
        builder.ConfigureNativeOperationValidation(openApi);
        builder.Services.AddNativeOperations([typeof(Write), typeof(ResultWrite), typeof(Read), typeof(Handler)]);
        builder.Services.AddSingleton<ConstructionEvidence>();
        builder.Services.AddScoped<OwnedState>();
        await using var app = builder.Build();
        await Assert.That(app.Services.GetRequiredService<ConstructionEvidence>().Constructed).IsEqualTo(0);
    }

    private sealed class NativeFactory(bool addLateRawHandler = false) : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ConstructionEvidence>();
                services.AddScoped<OwnedState>();
                services.AddNativeOperations([typeof(Write), typeof(ResultWrite), typeof(Read), typeof(Handler)]);
                if (addLateRawHandler)
                    services.AddScoped<ICommandHandler<Write>, Handler>();
            });
        }
    }

    public sealed record Write : ICommand;
    public sealed record ResultWrite : ICommand<int>;
    public sealed record Read : IQuery<int>;
    public sealed class ConstructionEvidence
    {
        private int _constructed;
        private int _disposed;
        private int _executed;
        public int Constructed => Volatile.Read(ref _constructed);
        public int Disposed => Volatile.Read(ref _disposed);
        public int Executed => Volatile.Read(ref _executed);
        public void RecordConstruction() => Interlocked.Increment(ref _constructed);
        public void RecordDisposal() => Interlocked.Increment(ref _disposed);
        public int RecordExecution() => Interlocked.Increment(ref _executed);
    }
    public sealed class OwnedState : IDisposable
    {
        private readonly ConstructionEvidence _evidence;
        public OwnedState(ConstructionEvidence evidence)
        {
            _evidence = evidence;
            evidence.RecordConstruction();
        }
        public void Dispose() => _evidence.RecordDisposal();
    }
    public sealed class Handler(OwnedState owned, ConstructionEvidence evidence) : ICommandHandler<Write>, ICommandHandler<ResultWrite, int>, IQueryHandler<Read, int>
    {
        private int Execute()
        {
            GC.KeepAlive(owned);
            return evidence.RecordExecution();
        }
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken) => Task.FromResult(Execute());
        public Task<int> ExecuteAsync(ResultWrite command, CancellationToken cancellationToken) => Task.FromResult(Execute());
        public Task<int> QueryAsync(Read query, CancellationToken cancellationToken) => Task.FromResult(Execute());
    }
}
