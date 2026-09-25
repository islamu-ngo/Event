using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.IntegrationTests.Extensions;

public sealed class EventResourceClientRedirectTests
{
   [Test]
   public async Task RegisteredSplitClient_PreservesApiRedirect_WithoutFetchingExternalOrigin()
   {
       using var certificate = CreateCertificate();
       var resourceId = Guid.CreateVersion7();
       var marker = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
       var apiArrival = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
           TaskCreationOptions.RunContinuationsAsynchronously);
       var externalArrival = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
           TaskCreationOptions.RunContinuationsAsynchronously);
       var secondArrival = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
           TaskCreationOptions.RunContinuationsAsynchronously);

       await using var external = CreateHttpsApp(certificate);
       external.MapGet($"/visit/{marker}", (HttpContext context) =>
       {
           externalArrival.TrySetResult(CaptureHeaders(context));
           return Results.Redirect($"/landed/{marker}");
       });
       external.MapGet($"/landed/{marker}", (HttpContext context) =>
       {
           secondArrival.TrySetResult(CaptureHeaders(context));
           return Results.Ok();
       });
       await external.StartAsync();
       var externalUrl = Address(external).Replace("127.0.0.1", "localhost", StringComparison.Ordinal);
       var destination = $"{externalUrl}/visit/{marker}";

       await using var api = CreateHttpsApp(certificate);
       api.MapGet($"/api/eventresource/{resourceId:D}/access", (HttpContext context) =>
       {
           apiArrival.TrySetResult(CaptureHeaders(context));
           return Results.Redirect(destination);
       });
       await api.StartAsync();

       var configuration = new ConfigurationBuilder().AddInMemoryCollection(
           new Dictionary<string, string?> { ["ExploreApi:BaseUrl"] = Address(api) }).Build();
       var environment = Substitute.For<IWebHostEnvironment>();
       environment.EnvironmentName.Returns(Environments.Development);
       var supportSessionId = Guid.CreateVersion7();
       var supportStore = Substitute.For<IBffSupportAccessSessionStore>();
       supportStore.ResolveCurrentAsync(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(BffSupportAccessStoreResult.Stored(
               new BffSupportAccessSession(supportSessionId, "owner", null,
                   Guid.CreateVersion7(), 1, false, DateTimeOffset.UtcNow.AddMinutes(10)))));
       var tenant = Substitute.For<ITenantRouteContextAccessor>();
       tenant.TenantSlug.Returns("tenant-alpha");
       var httpContext = new DefaultHttpContext();
       httpContext.Request.Host = new HostString("bff.internal.example");
       var contextAccessor = new HttpContextAccessor { HttpContext = httpContext };

       var services = new ServiceCollection();
       services.AddLogging();
       services.AddSingleton<IHttpContextAccessor>(contextAccessor);
       services.AddSingleton(Substitute.For<ICircuitAccessTokenService>());
       services.AddSingleton(Substitute.For<ICircuitUserContext>());
       services.AddSingleton(Substitute.For<ICircuitTokenStore>());
       services.AddSingleton(tenant);
       services.AddSingleton(Substitute.For<ISetupSecretResolver>());
       services.AddSingleton(supportStore);
       services.AddApiHttpClients(configuration, environment);
       await using var provider = services.BuildServiceProvider();
       var client = provider.GetRequiredService<IEventResourcesClient>();

       ApiException? redirect = null;
       try
       {
           await client.GetEventResourceAccessAsync(resourceId).WaitAsync(TimeSpan.FromSeconds(10));
       }
       catch (ApiException exception)
       {
           redirect = exception;
       }

       var apiHeaders = await apiArrival.Task.WaitAsync(TimeSpan.FromSeconds(10));
       await Assert.That(apiHeaders["X-Tenant-Slug"]).IsEqualTo("tenant-alpha");
       await Assert.That(apiHeaders["X-Forwarded-Host"]).IsEqualTo("bff.internal.example");
       await Assert.That(apiHeaders["X-Support-Access-Session-Id"]).IsEqualTo(supportSessionId.ToString("D"));
       // If the transport follows the API redirect, inspect both hops before reporting
       // the failure; a cross-origin redirect must never carry BFF-only headers.
       if (externalArrival.Task.IsCompleted)
           await secondArrival.Task.WaitAsync(TimeSpan.FromSeconds(10));

       foreach (var arrival in new[] { externalArrival.Task, secondArrival.Task })
       {
           if (!arrival.IsCompletedSuccessfully)
               continue;

           var headers = arrival.Result;
           foreach (var forbidden in new[]
                    {
                        "Authorization", "Cookie", "X-Tenant-Slug", "X-Tenant-Id",
                        "X-Support-Access-Mode", "X-Support-Access-Session-Id",
                        "X-Correlation-ID", "X-Forwarded-Host", "X-Forwarded-For", "Forwarded"
                    })
               await Assert.That(headers.ContainsKey(forbidden)).IsFalse();
       }

       await Assert.That(redirect).IsNotNull();
       await Assert.That(redirect!.StatusCode).IsEqualTo(StatusCodes.Status302Found);
       await Assert.That(redirect.Headers["Location"].Single()).IsEqualTo(destination);
       await Assert.That(externalArrival.Task.IsCompleted).IsFalse();
       await Assert.That(secondArrival.Task.IsCompleted).IsFalse();
   }

   private static IReadOnlyDictionary<string, string> CaptureHeaders(HttpContext context) =>
       context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(),
           StringComparer.OrdinalIgnoreCase);

   private static WebApplication CreateHttpsApp(X509Certificate2 certificate)
   {
       var builder = WebApplication.CreateBuilder(new WebApplicationOptions
       {
           EnvironmentName = Environments.Development
       });
       builder.WebHost.UseKestrel(options =>
           options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
       return builder.Build();
   }

   private static string Address(WebApplication app) => app.Services.GetRequiredService<IServer>()
       .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

   private static X509Certificate2 CreateCertificate()
   {
       using var key = RSA.Create(2048);
       var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256,
           RSASignaturePadding.Pkcs1);
       request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
       request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
       request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
       var names = new SubjectAlternativeNameBuilder();
       names.AddDnsName("localhost");
       names.AddIpAddress(IPAddress.Loopback);
       request.CertificateExtensions.Add(names.Build());
       return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
   }
}
