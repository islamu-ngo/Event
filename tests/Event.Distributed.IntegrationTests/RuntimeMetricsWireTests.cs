using System.Globalization;
using System.Net;
using System.Text.Json;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Distributed.IntegrationTests;

public sealed class RuntimeMetricsWireTests
{
    [Test]
    public async Task PrivateExporterPreservesCpuAndWorkingSetMetadata()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalEndpoints:Address"] = "127.0.0.1"
        });
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        app.Configuration["OperationalEndpoints:Port"] =
            new Uri(address).Port.ToString(CultureInfo.InvariantCulture);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        string exposition = await client.GetStringAsync("/metrics");
        var definitions = K6LoadRunner.ParseRuntimeMetricInventory(exposition);
        string actual = JsonSerializer.Serialize(definitions);

        await Assert.That(definitions.Count(metric => metric.Name == "dotnet_process_cpu_time_seconds_total"
            && metric.Kind == "counter" && metric.Unit == "seconds")).IsEqualTo(1).Because(actual);
        await Assert.That(definitions.Count(metric => metric.Name == "dotnet_process_memory_working_set_bytes"
            && metric.Kind == "gauge" && metric.Unit == "bytes")).IsEqualTo(1).Because(actual);
    }
}
