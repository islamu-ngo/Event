namespace Explore.Secrets.UnitTests.SetupLive;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Configuration;
using Explore.Secrets.Infrastructure;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public sealed class InfisicalSdkFailureBoundaryTests
{
    [Test]
    public async Task PinnedSdkUsesLocalReadProtocol()
    {
        await using var server = await LocalProvider.StartAsync(200);
        var client = new InfisicalClient(new InfisicalSdkSettingsBuilder()
            .WithHostUri(server.Url).Build());
        await client.Auth().UniversalAuth().LoginAsync(server.ClientId, server.ClientSecret);
        var secrets = await client.Secrets().ListAsync(new ListSecretsOptions
        {
            ProjectId = Guid.CreateVersion7().ToString(),
            EnvironmentSlug = "test",
            SecretPath = "/",
            Recursive = false,
            ExpandSecretReferences = true,
            ViewSecretValue = true
        });
        await Assert.That(secrets!.Single().Id == server.SecretId).IsTrue();
        await Assert.That(secrets.Single().SecretValue == server.Canary).IsTrue();
        await Assert.That(server.WriteCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(400, "Invalid", true)]
    [Arguments(401, "Unauthorized", true)]
    [Arguments(403, "Unauthorized", true)]
    [Arguments(404, "Missing", true)]
    [Arguments(503, "Unavailable", false)]
    public async Task BoundaryCategorizesActualHttpFailures(
        int status, string expectedKind, bool definiteRejection)
    {
        foreach (bool loginFailure in new[] { true, false })
        {
            await using var server = await LocalProvider.StartAsync(status, loginFailure);
            await using var factory = Factory(server);
            Exception? failure = null;
            byte[] value = Encoding.UTF8.GetBytes(server.Canary);
            try
            {
                var client = await factory.GetClientAsync();
                await client!.WriteSecretAsync("test", "/", "CANARY", value);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(value);
            }

            await AssertBoundedFailure(failure, server, expectedKind, definiteRejection);
            if (!loginFailure)
            {
                var client = await factory.GetClientAsync();
                failure = null;
                try { await client!.GetSecretAsync("test", "/", "CANARY"); }
                catch (Exception exception) { failure = exception; }
                await AssertBoundedFailure(failure, server, expectedKind, definiteRejection);
            }
        }
    }

    private static async Task AssertBoundedFailure(
        Exception? failure, LocalProvider server, string expectedKind, bool definiteRejection)
    {
            // Assert only bounded data, so a red run cannot print provider diagnostics.
            await Assert.That(failure is InfisicalOperationException).IsTrue();
            var bounded = (InfisicalOperationException)failure!;
            await Assert.That(bounded.FailureKind.ToString()).IsEqualTo(expectedKind);
            await Assert.That(bounded.DefiniteRejection).IsEqualTo(definiteRejection);
            await Assert.That(bounded.InnerException is null).IsTrue();
            await Assert.That(bounded.ToString().Contains(server.Canary, StringComparison.Ordinal)).IsFalse();
            await Assert.That(bounded.ToString().Contains(server.ClientSecret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(ResponseMode.Malformed, "Invalid")]
    [Arguments(ResponseMode.Disconnect, "Unavailable")]
    public async Task UncertainCompletionNeverClaimsRemoteRejection(ResponseMode mode, string expectedKind)
    {
        foreach (bool loginFailure in new[] { true, false })
        {
            await using var server = await LocalProvider.StartAsync(200, loginFailure, mode);
            await using var factory = Factory(server);
            Exception? failure = null;
            byte[] value = Encoding.UTF8.GetBytes(server.Canary);
            try
            {
                var client = await factory.GetClientAsync();
                await client!.WriteSecretAsync("test", "/", "CANARY", value);
            }
            catch (Exception exception) { failure = exception; }
            finally { CryptographicOperations.ZeroMemory(value); }
            await AssertBoundedFailure(failure, server, expectedKind, false);
            if (!loginFailure)
            {
                var client = (await factory.GetClientAsync())!;
                failure = null;
                try { await client.GetSecretAsync("test", "/", "CANARY"); }
                catch (Exception exception) { failure = exception; }
                await AssertBoundedFailure(failure, server, expectedKind, false);
            }
        }
    }

    [Test]
    public async Task CancellationBeforeDispatchDoesNotSendCredentialsOrValues()
    {
        await using var server = await LocalProvider.StartAsync(200);
        await using var factory = Factory(server);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await factory.GetClientAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(server.LoginCount).IsEqualTo(0);
        var client = (await factory.GetClientAsync())!;
        byte[] value = Encoding.UTF8.GetBytes(server.Canary);
        try
        {
            await Assert.That(async () => await client.WriteSecretAsync(
                "test", "/", "CANARY", value, cancellation.Token)).Throws<OperationCanceledException>();
            await Assert.That(async () => await factory.GetClientAsync(cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(server.WriteCount).IsEqualTo(0);
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }

    [Test]
    public async Task CancellationRemainsObservableAfterDispatch()
    {
        foreach (bool loginFailure in new[] { true, false })
        {
            await using var server = await LocalProvider.StartAsync(200, loginFailure, ResponseMode.Hold);
            await using var factory = Factory(server);
            using var cancellation = new CancellationTokenSource();
            byte[] value = Encoding.UTF8.GetBytes(server.Canary);
            try
            {
                Task operation = loginFailure
                    ? factory.GetClientAsync(cancellation.Token)
                    : (await factory.GetClientAsync())!.WriteSecretAsync(
                        "test", "/", "CANARY", value, cancellation.Token);
                await server.Dispatched.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                await Assert.That(async () => await operation).Throws<OperationCanceledException>();
            }
            finally { CryptographicOperations.ZeroMemory(value); }
        }
    }

    [Test]
    public async Task SuccessfulProtocolPreservesReadRevisionAndWritePayload()
    {
        await using var server = await LocalProvider.StartAsync(200);
        await using var factory = Factory(server);
        var client = (await factory.GetClientAsync())!;
        var secret = await client.GetSecretAsync("test", "/", "CANARY");
        await Assert.That(secret!.Value == server.Canary).IsTrue();
        await Assert.That(secret.Revision == $"{server.SecretId}:1").IsTrue();
        await Assert.That(await client.GetSecretAsync("test", "/", "ABSENT")).IsNull();
        byte[] value = Encoding.UTF8.GetBytes(server.Canary);
        try
        {
            await Assert.That(await client.WriteSecretAsync("test", "/", "CANARY", value)).IsTrue();
            await Assert.That(server.ExactWrite).IsTrue();
            var sdk = new InfisicalClient(new InfisicalSdkSettingsBuilder().WithHostUri(server.Url).Build());
            await sdk.Auth().UniversalAuth().LoginAsync(server.ClientId, server.ClientSecret);
            var written = await sdk.Secrets().UpdateAsync(new UpdateSecretOptions
            {
                ProjectId = Guid.CreateVersion7().ToString(),
                EnvironmentSlug = "test", SecretPath = "/", SecretName = "CANARY",
                NewSecretValue = server.Canary
            });
            await Assert.That(written is not null).IsTrue();
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }

    [Test]
    public async Task MalformedUtf8IsLocalInvalidWithoutRemoteRejection()
    {
        await using var server = await LocalProvider.StartAsync(200);
        await using var factory = Factory(server);
        var client = (await factory.GetClientAsync())!;
        byte[] value = [0xC3, 0x28];
        Exception? failure = null;
        try { await client.WriteSecretAsync("test", "/", "CANARY", value); }
        catch (Exception exception) { failure = exception; }
        finally { CryptographicOperations.ZeroMemory(value); }
        await AssertBoundedFailure(failure, server, "Invalid", false);
        await Assert.That(server.WriteCount).IsEqualTo(0);
    }

    private static InfisicalClientFactory Factory(LocalProvider server) => new(
        NullLogger<InfisicalClientFactory>.Instance,
        Options.Create(new SecretProviderOptions
        {
            Infisical = new InfisicalOptions
            {
                Url = server.Url,
                ClientId = server.ClientId,
                ClientSecret = server.ClientSecret,
                ProjectId = Guid.CreateVersion7().ToString(),
                Environment = "test"
            }
        }));

    [Test]
    [Arguments(400)]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(404)]
    [Arguments(503)]
    public async Task PinnedSdkExposesActualLoginFailureShape(int status)
    {
        await using var server = await LocalProvider.StartAsync(status, loginFailure: true);
        var client = new InfisicalClient(new InfisicalSdkSettingsBuilder()
            .WithHostUri(server.Url).Build());
        Exception? failure = null;
        try
        {
            await client.Auth().UniversalAuth().LoginAsync(server.ClientId, server.ClientSecret);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        Console.WriteLine($"SDK login HTTP {status}: type={failure!.GetType().FullName}; " +
            $"structuredStatus={(failure as HttpRequestException)?.StatusCode?.ToString() ?? "none"}");
        Console.WriteLine("SDK exception public properties: " + string.Join(",",
            failure.GetType().GetProperties().Select(property => property.Name)));
        Console.WriteLine("SDK settings public methods: " + string.Join(",",
            typeof(InfisicalSdkSettingsBuilder).GetMethods()
                .Where(method => method.DeclaringType == typeof(InfisicalSdkSettingsBuilder))
                .Select(method => method.Name)));
        await Assert.That(server.LoginCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(400)]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(404)]
    [Arguments(503)]
    public async Task PinnedSdkExposesActualWriteFailureShape(int status)
    {
        await using var server = await LocalProvider.StartAsync(status);
        var client = new InfisicalClient(new InfisicalSdkSettingsBuilder()
            .WithHostUri(server.Url).Build());
        await client.Auth().UniversalAuth().LoginAsync(server.ClientId, server.ClientSecret);
        Exception? failure = null;
        try
        {
            await client.Secrets().UpdateAsync(new UpdateSecretOptions
            {
                ProjectId = Guid.CreateVersion7().ToString(),
                EnvironmentSlug = "test",
                SecretPath = "/",
                SecretName = "CANARY",
                NewSecretValue = server.Canary
            });
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        Console.WriteLine($"SDK write HTTP {status}: type={failure!.GetType().FullName}; " +
            $"structuredStatus={(failure as HttpRequestException)?.StatusCode?.ToString() ?? "none"}");
        await Assert.That(server.WriteCount).IsEqualTo(1);
    }

    public enum ResponseMode { Json, Malformed, Disconnect, Hold }

    private sealed class LocalProvider : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private int _loginCount;
        private int _writeCount;
        public string Url => _app.Urls.Single();
        public string ClientId { get; } = Guid.CreateVersion7().ToString();
        public string ClientSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public string Canary { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public string SecretId { get; } = Guid.CreateVersion7().ToString();
        public TaskCompletionSource Dispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ExactWrite { get; private set; }
        public int LoginCount => Volatile.Read(ref _loginCount);
        public int WriteCount => Volatile.Read(ref _writeCount);

        private LocalProvider(WebApplication app) => _app = app;

        public static async Task<LocalProvider> StartAsync(
            int status, bool loginFailure = false, ResponseMode mode = ResponseMode.Json)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var provider = new LocalProvider(builder.Build());
            provider._app.Run(async context =>
            {
                bool login = context.Request.Path.Value!.Contains("universal-auth", StringComparison.Ordinal);
                Console.WriteLine($"SDK wire method={context.Request.Method}; " +
                    $"route={context.Request.Path.Value!.Replace("CANARY", "{name}", StringComparison.Ordinal)}");
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    Console.WriteLine("SDK wire query fields=" + string.Join(",", context.Request.Query.Keys));
                }
                if (!HttpMethods.IsGet(context.Request.Method))
                {
                    using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
                    Console.WriteLine("SDK wire fields=" + string.Join(",",
                        body.RootElement.EnumerateObject().Select(property => property.Name)));
                    if (HttpMethods.IsPatch(context.Request.Method))
                        provider.ExactWrite = body.RootElement.GetProperty("secretValue").GetString() == provider.Canary
                            && body.RootElement.GetProperty("secretName").GetString() == "CANARY"
                            && body.RootElement.GetProperty("environment").GetString() == "test"
                            && body.RootElement.GetProperty("secretPath").GetString() == "/";
                }
                if (login)
                {
                    Interlocked.Increment(ref provider._loginCount);
                }
                else
                {
                    Interlocked.Increment(ref provider._writeCount);
                }

                context.Response.StatusCode = login && !loginFailure ? 200 : status;
                if (!login || loginFailure)
                {
                    provider.Dispatched.TrySetResult();
                    if (mode == ResponseMode.Disconnect) { context.Abort(); return; }
                    if (mode == ResponseMode.Malformed)
                    {
                        await context.Response.WriteAsync("{");
                        return;
                    }
                    if (mode == ResponseMode.Hold)
                    {
                        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        using var registration = context.RequestAborted.Register(() => disconnected.TrySetResult());
                        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        return;
                    }
                }
                if (login && !loginFailure)
                {
                    await context.Response.WriteAsJsonAsync(new
                    {
                        accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                        expiresIn = 3600,
                        accessTokenTTL = 3600,
                        tokenType = "Bearer"
                    });
                }
                else if (status == 200)
                {
                    var secret = new
                    {
                        id = provider.SecretId, secretKey = "CANARY", secretValue = provider.Canary, version = 1
                    };
                    if (HttpMethods.IsGet(context.Request.Method))
                        await context.Response.WriteAsJsonAsync(new { secrets = new[] { secret } });
                    else
                        await context.Response.WriteAsJsonAsync(new { secret });
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(new { message = provider.Canary });
                }
            });
            await provider._app.StartAsync();
            return provider;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
