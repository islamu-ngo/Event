// ABOUTME: Exercises collection of caller-owned shutdown state after the API startup lifecycle ends.
// ABOUTME: Detects process-wide signal subscriptions retaining disposed hosts across repeated composition.

using System.Runtime.CompilerServices;
using Explore.API.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Event.Api.IntegrationTests.Hosting;

public sealed class ApiHostLifetimeTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DisposedHost_ReleasesCallerOwnedShutdownState(bool registerApiStartup, bool startHost)
    {
        WeakReference shutdownState = StartAndDisposeHost(registerApiStartup, startHost);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Assert.That(shutdownState.IsAlive).IsFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StartAndDisposeHost(bool registerApiStartup, bool startHost)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.AddServiceDefaults();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        using var shutdown = new CancellationTokenSource();
        try
        {
            if (registerApiStartup)
                app.RunApiHostStartupAsync(new(IsOpenApiGeneration: true, UseQuartzScheduler: false,
                    HttpsRedirectionEnabled: false), shutdown, () => { }).GetAwaiter().GetResult();
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
        }
    }
}
