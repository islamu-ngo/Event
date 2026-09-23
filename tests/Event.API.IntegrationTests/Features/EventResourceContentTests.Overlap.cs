using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed partial class EventResourceContentTests
{
    [Test]
    public async Task CommittedRevocationDeniesNewRangeWhileAlreadyStartedStreamCompletes()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pipe = new Pipe();
        await using var stream = pipe.Reader.AsStream();
        int split = seed.Bytes.Length / 2;
        await pipe.Writer.WriteAsync(seed.Bytes.AsMemory(0, split), deadline.Token);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(new FileStorageReadResult(stream, EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddSingleton<IEventResourceAuthorizationProvider>(new AlwaysAllowProvider());
            ConfigureStorageProviders(services, seed.BindingId, provider);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        await AuthenticateAsync(client, seed.Credentials);

        string path = $"/api/eventresource/{seed.ResourceId}/content";
        using var started = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        try
        {
            await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(started.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
            await using var revoke = factory.CreateDatabase();
            revoke.EnableTenantFilterBypass("Revoke exact participant coverage after response headers started.");
            await Assert.That(await revoke.EventRegistrations.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.RegistrationOrderId == seed.OrderId)
                .ExecuteDeleteAsync(deadline.Token)).IsEqualTo(1);
            using var range = new HttpRequestMessage(HttpMethod.Get, path);
            range.Headers.Range = new RangeHeaderValue(0, 1);
            using var denied = await client.SendAsync(range, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        }
        finally
        {
            await pipe.Writer.WriteAsync(seed.Bytes.AsMemory(split), deadline.Token);
            await pipe.Writer.CompleteAsync();
        }

        await Assert.That(await started.Content.ReadAsByteArrayAsync(deadline.Token)).IsEquivalentTo(seed.Bytes);
    }
}
