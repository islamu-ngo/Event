using Explore.Application.Constants;
namespace Event.Api.IntegrationTests.Features;

using System.Text;
using System.Security.Claims;
using Explore.API.Authentication;
using Explore.API.Attributes;
using Explore.API.Middleware;
using Explore.API.OpenApi;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Microsoft.OpenApi;

public class IdempotencyMiddlewareTests
{
    [Test]
    public async Task Middleware_WhenRequiredKeyIsMissing_ReturnsProblemDetailsWithoutInvokingNext()
    {
        var result = await InvokeAsync(idempotencyKey: null);

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(result.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(result.Body).Contains("Idempotency-Key is required.");
        await Assert.That(result.NextCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Middleware_WhenRequiredKeyIsWhitespace_ReturnsProblemDetailsWithoutInvokingNext()
    {
        var result = await InvokeAsync(idempotencyKey: " \t");

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(result.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(result.Body).Contains("Idempotency-Key is required.");
        await Assert.That(result.NextCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Middleware_WhenRequiredAiMessageKeyIsMissingOrWhitespace_ReturnsProblemDetailsWithoutInvokingNext()
    {
        var missing = await InvokeAsync(idempotencyKey: null, path: "/api/ai/assistant/conversations/test/messages");
        var whitespace = await InvokeAsync(idempotencyKey: " \t", path: "/api/ai/assistant/conversations/test/messages");

        await Assert.That(missing.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(missing.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(missing.Body).Contains("Idempotency-Key is required.");
        await Assert.That(missing.NextCallCount).IsEqualTo(0);
        await Assert.That(whitespace.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(whitespace.ContentType).IsEqualTo("application/problem+json");
        await Assert.That(whitespace.Body).Contains("Idempotency-Key is required.");
        await Assert.That(whitespace.NextCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Middleware_WhenUnmarkedAiMessageHasKey_PreservesApplicationManagedBypass()
    {
        var result = await InvokeAsync(
            idempotencyKey: "application-managed-key",
            requiresIdempotencyKey: false,
            path: "/api/ai/assistant/conversations/test/messages");

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(result.NextCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task OpenApiMetadata_WhenIdempotencyKeyIsRequired_EmitsTrueExtension()
    {
        var operation = new OpenApiOperation();

        var applied = EndpointClassificationTransformer.ApplyIdempotencyKeyRequirement(
            operation,
            [new RequireIdempotencyKeyAttribute()]);

        await Assert.That(applied).IsTrue();
        var extension = operation.Extensions!["x-idempotency-key-required"] as JsonNodeExtension;
        await Assert.That(extension).IsNotNull();
        await Assert.That(extension!.Node.GetValue<bool>()).IsTrue();
        OpenApiParameter parameter = (OpenApiParameter)operation.Parameters!.Single(candidate =>
            candidate.In == ParameterLocation.Header && candidate.Name == "Idempotency-Key");
        await Assert.That(parameter.Required).IsTrue();
    }

    [Test]
    public async Task OpenApiMetadata_AuthenticatedPaymentMutation_MarksHeaderRequired()
    {
        var parameter = new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
        var operation = new OpenApiOperation
        {
            OperationId = RouteNames.StartAuthenticatedRegistrationPayment,
            Parameters = [parameter]
        };

        EndpointClassificationTransformer.ApplyIdempotencyKeyRequirement(
            operation,
            [new RequireIdempotencyKeyAttribute()]);

        await Assert.That(parameter.Required).IsTrue();
    }

    [Test]
    public async Task Middleware_WhenKeyIsOptionalAndMissing_PassesThroughNormally()
    {
        var result = await InvokeAsync(idempotencyKey: null, requiresIdempotencyKey: false);

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(result.NextCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Middleware_WhenMatchingKeyIsInProgress_DoesNotExecuteDuplicate()
    {
        var repository = new InMemoryIdempotencyRepository();
        var tenantId = Guid.CreateVersion7();
        var firstRequestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<MiddlewareResult> firstRequest = InvokeAsync(
            "same-key",
            repository: repository,
            tenantId: tenantId,
            next: async context =>
            {
                firstRequestStarted.SetResult(true);
                await releaseFirstRequest.Task;
                context.Response.StatusCode = StatusCodes.Status201Created;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"created\":true}");
            });

        await firstRequestStarted.Task;

        MiddlewareResult duplicate = await InvokeAsync(
            "same-key",
            repository: repository,
            tenantId: tenantId,
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                return Task.CompletedTask;
            });

        releaseFirstRequest.SetResult(true);
        MiddlewareResult completed = await firstRequest;

        await Assert.That(completed.StatusCode).IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(duplicate.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(duplicate.Body).Contains("idempotency_request_in_progress");
        await Assert.That(completed.NextCallCount + duplicate.NextCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SuppressedOneTimeSecretResponseBypassesGenericStorageAndReplay()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers["Idempotency-Key"] = "one-time-secret";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new SuppressIdempotencyResponseStorageAttribute()),
            "one-time-secret"));
        var nextCallCount = 0;
        var middleware = new IdempotencyMiddleware(
            async current =>
            {
                nextCallCount++;
                current.Response.StatusCode = StatusCodes.Status200OK;
                await current.Response.WriteAsync("{\"plaintext\":\"must-not-be-stored\"}");
            },
            new RecyclableMemoryStreamManager(),
            NullLogger<IdempotencyMiddleware>.Instance,
            new EphemeralDataProtectionProvider());

        await middleware.InvokeAsync(context);

        await Assert.That(nextCallCount).IsEqualTo(1);
        await Assert.That(context.Response.Headers.ContainsKey("X-Idempotency-Replay")).IsFalse();
    }

    [Test]
    public async Task ScannerPrincipalFingerprintBindsTheAuthenticatedCapabilityIdentity()
    {
        Guid firstCapabilityId = Guid.CreateVersion7();
        Guid secondCapabilityId = Guid.CreateVersion7();
        var streams = new RecyclableMemoryStreamManager();
        IdempotencyRequestIdentity first = await Identity(firstCapabilityId);
        IdempotencyRequestIdentity second = await Identity(secondCapabilityId);

        await Assert.That(first.UserId).IsNull();
        await Assert.That(second.UserId).IsNull();
        await Assert.That(first.PrincipalFingerprint).IsNotEqualTo(second.PrincipalFingerprint);
        await Assert.That(first.PrincipalFingerprint).DoesNotContain(firstCapabilityId.ToString("N"));
        await Assert.That(second.PrincipalFingerprint).DoesNotContain(secondCapabilityId.ToString("N"));

        async Task<IdempotencyRequestIdentity> Identity(Guid capabilityId)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"credential\":\"redacted\"}"));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    AdmissionScannerAuthenticationDefaults.CapabilityIdClaim,
                    capabilityId.ToString("D"))
            ], ApiAuthenticationSchemeNames.AdmissionScanner));
            return await IdempotencyRequestIdentityFactory.CreateAsync(
                context,
                streams,
                CancellationToken.None);
        }
    }

    [Test]
    public async Task Middleware_WhenClaimPersistenceFails_FailsClosedBeforeExecuting()
    {
        MiddlewareResult result = await InvokeAsync(
            "same-key",
            repository: new FailingIdempotencyRepository());

        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(result.Body).Contains("idempotency_unavailable");
        await Assert.That(result.NextCallCount).IsEqualTo(0);
    }

    private static async Task<MiddlewareResult> InvokeAsync(
        string? idempotencyKey,
        bool requiresIdempotencyKey = true,
        string path = "/api/idempotency-test",
        IIdempotencyRepository? repository = null,
        Guid? tenantId = null,
        RequestDelegate? next = null)
    {
        var idempotencyRepository = repository ?? new InMemoryIdempotencyRepository();
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new StaticTenantContext(tenantId ?? Guid.CreateVersion7()))
            .AddSingleton(idempotencyRepository)
            .AddSingleton<IProblemDetailsService, IdempotencyMiddlewareRealRuntimeTests.JsonProblemDetailsService>()
            .BuildServiceProvider();
        var nextCallCount = 0;
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        context.Response.Body = new MemoryStream();
        if (idempotencyKey is not null)
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        if (requiresIdempotencyKey)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new RequireIdempotencyKeyAttribute()),
                "required-idempotency-key"));
        }

        RequestDelegate effectiveNext = next ??
            (httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                return Task.CompletedTask;
            });
        var middleware = new IdempotencyMiddleware(
            httpContext =>
            {
                nextCallCount++;
                return effectiveNext(httpContext);
            },
            new RecyclableMemoryStreamManager(),
            NullLogger<IdempotencyMiddleware>.Instance,
            new EphemeralDataProtectionProvider());

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return new MiddlewareResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            await reader.ReadToEndAsync(),
            nextCallCount);
    }

    private sealed record MiddlewareResult(int StatusCode, string? ContentType, string Body, int NextCallCount);

    private sealed class StaticTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<(Guid TenantId, string Key), IdempotencyRecord> _records = [];

        public Task<IdempotencyRecord?> FindAsync(string key, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault((tenantId, key)));

        public Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
        {
            _records[(record.TenantId, record.Key)] = record;
            return Task.CompletedTask;
        }

        public Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_records.TryGetValue((record.TenantId, record.Key), out var existing))
                {
                    return Task.FromResult(new IdempotencyClaim(existing, IsOwner: false));
                }

                _records[(record.TenantId, record.Key)] = record;
                return Task.FromResult(new IdempotencyClaim(record, IsOwner: true));
            }
        }

        public Task<bool> CompleteAsync(
            Guid recordId,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default)
        {
            var record = _records.Values.FirstOrDefault(candidate => candidate.Id == recordId);
            if (record is null || record.StatusCode != IdempotencyRecord.InProgressStatusCode)
            {
                return Task.FromResult(false);
            }

            record.StatusCode = statusCode;
            record.ResponseBody = responseBody;
            record.ContentType = contentType;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseAsync(Guid recordId, CancellationToken cancellationToken = default)
        {
            var pair = _records.FirstOrDefault(candidate => candidate.Value.Id == recordId);
            if (pair.Value is null || pair.Value.StatusCode != IdempotencyRecord.InProgressStatusCode)
            {
                return Task.FromResult(false);
            }

            _records.Remove(pair.Key);
            return Task.FromResult(true);
        }

        public Task<int> CountExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class FailingIdempotencyRepository : IIdempotencyRepository
    {
        public Task<IdempotencyRecord?> FindAsync(
            string key,
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdempotencyRecord?>(null);

        public Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<bool> CompleteAsync(
            Guid recordId,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<bool> ReleaseAsync(Guid recordId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<int> CountExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> DeleteExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

}
