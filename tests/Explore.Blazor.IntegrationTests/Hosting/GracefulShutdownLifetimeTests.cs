// ABOUTME: Exercises disposal of Split BFF process-signal registrations through the real host lifecycle.
// ABOUTME: Detects retained shutdown state in both stopped and never-started hosts.

using System.Runtime.CompilerServices;
using Explore.Blazor.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.IntegrationTests.Hosting;

public sealed class GracefulShutdownLifetimeTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DisposedHost_ReleasesShutdownState(bool registerSignals, bool startHost)
    {
        WeakReference shutdownState = StartAndDisposeHost(registerSignals, startHost);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Assert.That(shutdownState.IsAlive).IsFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StartAndDisposeHost(bool registerSignals, bool startHost)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.AddServiceDefaults();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var shutdown = new GracefulShutdownState();
        try
        {
            if (registerSignals) app.ConfigureGracefulShutdown(shutdown);
            if (startHost)
            {
                app.StartAsync().GetAwaiter().GetResult();
                app.StopAsync().GetAwaiter().GetResult();
            }
            return new WeakReference(shutdown);
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            // A failing retention regression must not sleep in the leaked ProcessExit callback.
            shutdown.IsShuttingDown = true;
            shutdown.CancellationTokenSource.Dispose();
        }
    }
}
