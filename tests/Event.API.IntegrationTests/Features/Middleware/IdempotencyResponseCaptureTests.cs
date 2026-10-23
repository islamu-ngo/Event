using System.Net;
using System.Security.Claims;
using System.Text;
using Explore.API.Middleware;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class IdempotencyResponseCaptureTests
{
    private const int Limit = 1024 * 1024;

    [Test]
    [Arguments(Limit - 1, false)]
    [Arguments(Limit, false)]
    [Arguments(Limit + 1, false)]
    [Arguments(Limit + 1, true)]
    public async Task HttpResponsePreservesBytesWhileUnavailableReceiptCannotReexecute(int size, bool knownLength)
    {
        string path = Path.Combine(Path.GetTempPath(), $"receipt-http-{Guid.CreateVersion7():N}.db");
        var tenant = new SelectedTenant(Guid.CreateVersion7());
        ExploreDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>()
                .EnableServiceProviderCaching(false)
                .UseSqlite($"Data Source={path};Pooling=False").UseSnakeCaseNamingConvention().Options;
            var context = new ExploreDbContext(options) { TenantContext = tenant };
            return context;
        }
        try
        {
            await using (var context = CreateContext())
            {
                await context.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Services.AddProblemDetails();
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<ITenantContext>(tenant);
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddSingleton(new RecyclableMemoryStreamManager());
            builder.Services.AddScoped(_ => CreateContext());
            builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
            await using var app = builder.Build();
            string user = Guid.CreateVersion7().ToString("D");
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "boundary"));
                await next(context);
            });
            app.UseMiddleware<IdempotencyMiddleware>();
            byte[] body = Encoding.UTF8.GetBytes(new string('x', size));
            int executions = 0;
            long retained = -1;
            app.Run(async context =>
            {
                Interlocked.Increment(ref executions);
                context.Response.StatusCode = StatusCodes.Status201Created;
                context.Response.ContentType = "application/json";
                context.Response.Headers["X-Response-Marker"] = "preserved";
                if (knownLength) context.Response.ContentLength = body.Length;
                int prefix = Math.Min(Limit, size);
                await context.Response.Body.WriteAsync(body.AsMemory(0, prefix));
                await context.Response.Body.WriteAsync(body.AsMemory(prefix));
                retained = context.Response.Body.Length;
            });
            await app.StartAsync();
            using var client = app.GetTestClient();
            HttpRequestMessage Request()
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/capture");
                request.Headers.Add("Idempotency-Key", "http-boundary");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return request;
            }

            using var firstRequest = Request();
            using var first = await client.SendAsync(firstRequest).WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That((await first.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
            await Assert.That(first.Headers.GetValues("X-Response-Marker").Single()).IsEqualTo("preserved");
            await Assert.That(retained <= Limit).IsTrue();
            if (size > Limit) await Assert.That(retained).IsEqualTo(0);

            using var retryRequest = Request();
            using var retry = await client.SendAsync(retryRequest).WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(retry.StatusCode).IsEqualTo(size <= Limit ? HttpStatusCode.Created : HttpStatusCode.Conflict);
            if (size <= Limit)
            {
                await Assert.That((await retry.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
                await Assert.That(retry.Headers.GetValues("X-Idempotency-Replay").Single()).IsEqualTo("true");
            }
            await Assert.That(executions).IsEqualTo(1);
            await using var observer = CreateContext();
            var receipt = await observer.IdempotencyRecords.SingleAsync(row => row.Key == "http-boundary");
            await Assert.That(receipt.StatusCode).IsEqualTo(size <= Limit ? 201 : IdempotencyRecord.ResponseUnavailableStatusCode);
            await Assert.That(receipt.ResponseBody is null).IsEqualTo(size > Limit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
