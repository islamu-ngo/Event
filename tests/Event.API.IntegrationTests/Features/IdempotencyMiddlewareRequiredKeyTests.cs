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

    [Test]
    public async Task ReceiptStore_WhenDescriptorMismatches_DoesNotExposeExistingReceipt()
    {
        var repository = new InMemoryIdempotencyRepository();
        var now = DateTime.UtcNow;
        var record = new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Key = "receipt-store-key",
            UserId = "test-user",
            RequestMethod = HttpMethods.Post,
            RequestTarget = "/api/idempotency-test",
            RequestContentType = "application/json",
            RequestBodyHash = "original-body-hash",
            PrincipalFingerprint = "principal-fingerprint",
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };

        var claim = await repository.TryClaimAsync(record);
        using var owner = claim.Owner;
        var matching = await repository.LookupAsync(
            IdempotencyRequestDescriptor.FromRecord(record),
            now);
        var mismatch = await repository.LookupAsync(
            IdempotencyRequestDescriptor.FromRecord(record) with { RequestTarget = "/api/other" },
            now);

        await Assert.That(claim.IsOwner).IsTrue();
        await Assert.That(matching.Outcome).IsEqualTo(IdempotencyLookupOutcome.Existing);
        await Assert.That(matching.Record).IsEqualTo(record);
        await Assert.That(mismatch.Outcome).IsEqualTo(IdempotencyLookupOutcome.RequestMismatch);
        await Assert.That(mismatch.Record).IsNull();
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
            .AddSingleton<IProblemDetailsService, IdempotencyMiddlewareReceiptStateTests.JsonProblemDetailsService>()
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
        private readonly Dictionary<(Guid TenantId, string Key), Receipt> _receipts = [];

        public Task<IdempotencyLookup> LookupAsync(
            IdempotencyRequestDescriptor request,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(Lookup(request, utcNow));
            }
        }

        public Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var descriptor = IdempotencyRequestDescriptor.FromRecord(record);
                if (_receipts.ContainsKey((record.TenantId, record.Key)))
                {
                    return Task.FromResult(new IdempotencyClaim(
                        Lookup(descriptor, record.CreatedAt),
                        Owner: null));
                }

                var receipt = new Receipt(record, descriptor);
                var owner = new ReceiptOwner(this, descriptor, record.Id);
                receipt.Owner = owner;
                _receipts.Add((record.TenantId, record.Key), receipt);
                return Task.FromResult(new IdempotencyClaim(
                    new IdempotencyLookup(IdempotencyLookupOutcome.Absent, record),
                    owner));
            }
        }

        public Task<bool> CompleteAsync(
            IIdempotencyClaimOwner owner,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (owner is not ReceiptOwner receiptOwner
                    || !_receipts.TryGetValue(
                        (receiptOwner.Descriptor.TenantId, receiptOwner.Descriptor.Key),
                        out var receipt)
                    || !ReferenceEquals(receipt.Owner, receiptOwner)
                    || !receiptOwner.CanComplete(this, receipt.Descriptor, receipt.Record.Id)
                    || receipt.Record.StatusCode != IdempotencyRecord.InProgressStatusCode)
                {
                    return Task.FromResult(false);
                }

                receipt.Record.StatusCode = statusCode;
                receipt.Record.ResponseBody = responseBody;
                receipt.Record.ContentType = contentType;
                receiptOwner.Spend();
                return Task.FromResult(true);
            }
        }

        public Task<int> CountExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(_receipts.Values
                    .Select(receipt => receipt.Record)
                    .Where(record => record.ExpiresAt <= expiresBeforeUtc
                        && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                    .OrderBy(record => record.ExpiresAt)
                    .Take(batchSize)
                    .Count());
            }
        }

        public Task<int> ScrubExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var expired = _receipts.Values
                    .Select(receipt => receipt.Record)
                    .Where(record => record.ExpiresAt <= expiresBeforeUtc
                        && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                    .OrderBy(record => record.ExpiresAt)
                    .Take(batchSize)
                    .ToArray();
                foreach (var record in expired)
                {
                    record.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                    record.ResponseBody = null;
                    record.ContentType = null;
                }
                return Task.FromResult(expired.Length);
            }
        }

        private IdempotencyLookup Lookup(IdempotencyRequestDescriptor request, DateTime utcNow)
        {
            if (!_receipts.TryGetValue((request.TenantId, request.Key), out var receipt))
            {
                return new IdempotencyLookup(IdempotencyLookupOutcome.Absent, null);
            }

            ScrubExpiredReceipt(receipt.Record, utcNow);
            return receipt.Descriptor == request
                ? new IdempotencyLookup(IdempotencyLookupOutcome.Existing, receipt.Record)
                : new IdempotencyLookup(IdempotencyLookupOutcome.RequestMismatch, null);
        }

        private static void ScrubExpiredReceipt(IdempotencyRecord record, DateTime utcNow)
        {
            if (record.ExpiresAt <= utcNow
                && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
            {
                record.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                record.ResponseBody = null;
                record.ContentType = null;
            }
        }

        private sealed class Receipt(
            IdempotencyRecord record,
            IdempotencyRequestDescriptor descriptor)
        {
            public IdempotencyRecord Record { get; } = record;

            public IdempotencyRequestDescriptor Descriptor { get; } = descriptor;

            public ReceiptOwner? Owner { get; set; }
        }

        private sealed class ReceiptOwner(
            InMemoryIdempotencyRepository issuer,
            IdempotencyRequestDescriptor descriptor,
            Guid receiptId) : IIdempotencyClaimOwner
        {
            private readonly InMemoryIdempotencyRepository _issuer = issuer;
            private readonly Guid _receiptId = receiptId;
            private bool _disposed;
            private bool _spent;

            public IdempotencyRequestDescriptor Descriptor { get; } = descriptor;

            public bool CanComplete(
                InMemoryIdempotencyRepository issuer,
                IdempotencyRequestDescriptor descriptor,
                Guid receiptId) =>
                ReferenceEquals(_issuer, issuer)
                && Descriptor == descriptor
                && _receiptId == receiptId
                && !_disposed
                && !_spent;

            public void Spend() => _spent = true;

            public void Dispose() => _disposed = true;
        }
    }

    private sealed class FailingIdempotencyRepository : IIdempotencyRepository
    {
        public Task<IdempotencyLookup> LookupAsync(
            IdempotencyRequestDescriptor request,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<IdempotencyClaim> TryClaimAsync(
            IdempotencyRecord record,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<bool> CompleteAsync(
            IIdempotencyClaimOwner owner,
            int statusCode,
            string? responseBody,
            string? contentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<int> CountExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");

        public Task<int> ScrubExpiredAsync(
            DateTime expiresBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Idempotency storage is unavailable.");
    }

}
