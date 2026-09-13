using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEmailDispatchHttpTests
{
    private const string Root = "/api/admin/email-dispatch";

    [Test]
    public async Task ProcessorControlWritesPreserveIndependentFieldsAndSingletonIdentity()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        using var client = factory.Client();
        var initial = await ControlAsync(client);
        await Assert.That(initial.IsPaused).IsFalse();
        await Assert.That(initial.GlobalSmtpRateLimitPerMinuteOverride).IsNull();
        var paused = await CommandAsync(await client.PutAsync(Root + "/control/pause?reason=%20maintenance%20", null));
        var limited = await CommandAsync(await client.PutAsync(Root + "/control/rate-limit?rateLimitPerMinute=25", null));
        await Assert.That(limited.Id).IsEqualTo(paused.Id);
        var control = await ControlAsync(client);
        await Assert.That(control.IsPaused).IsTrue();
        await Assert.That(control.PauseReason).IsEqualTo("maintenance");
        await Assert.That(control.PausedAt).IsNotNull();
        await Assert.That(control.GlobalSmtpRateLimitPerMinuteOverride).IsEqualTo(25);
        var repeated = await CommandAsync(await client.PutAsync(Root + "/control/rate-limit?rateLimitPerMinute=25", null));
        await Assert.That(repeated.Id).IsEqualTo(paused.Id);
        await CommandAsync(await client.DeleteAsync(Root + "/control/pause"));
        control = await ControlAsync(client);
        await Assert.That(control.IsPaused).IsFalse();
        await Assert.That(control.PauseReason).IsNull();
        await Assert.That(control.PausedAt).IsNull();
        await Assert.That(control.GlobalSmtpRateLimitPerMinuteOverride).IsEqualTo(25);
        await CommandAsync(await client.DeleteAsync(Root + "/control/rate-limit"));
        await Assert.That((await ControlAsync(client)).GlobalSmtpRateLimitPerMinuteOverride).IsNull();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    [Arguments(EmailDispatchStatus.DeadLettered)]
    [Arguments(EmailDispatchStatus.Parked)]
    [Arguments(EmailDispatchStatus.RetryScheduled)]
    [Arguments(EmailDispatchStatus.Pending)]
    public async Task ReplayQueuesOnlyDurableStateAndPendingReplayIsIdempotent(EmailDispatchStatus status)
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(status);
        using var client = factory.Client(factory.TenantAdminId);
        await Assert.That((await CommandAsync(await client.PostAsync(ReplayUrl(id), null))).Id).IsEqualTo(id);
        var row = (await StatusAsync(factory)).Single();
        await Assert.That(row.DeliveryStatus).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(row.AttemptCount).IsEqualTo(3);
        if (status != EmailDispatchStatus.Pending)
        {
            await Assert.That(row.LastFailureCategory).IsNull();
            await Assert.That(row.ParkReason).IsNull();
            await Assert.That(row.ParkedAt).IsNull();
        }
        await Assert.That((await CommandAsync(await client.PostAsync(ReplayUrl(id), null))).Id).IsEqualTo(id);
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(row);
    }

    [Test]
    [Arguments(EmailDispatchStatus.Sent, false)]
    [Arguments(EmailDispatchStatus.Skipped, false)]
    [Arguments(EmailDispatchStatus.Processing, false)]
    [Arguments(EmailDispatchStatus.Unknown, false)]
    [Arguments(EmailDispatchStatus.Parked, true)]
    [Arguments(EmailDispatchStatus.Pending, true)]
    public async Task ReplayRejectsUnsafeStatesAndRedactedContentWithoutMutation(EmailDispatchStatus status, bool redacted)
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(status, redacted);
        var before = (await StatusAsync(factory)).Single();
        using var client = factory.Client();
        await ProblemAsync(await client.PostAsync(ReplayUrl(id), null), HttpStatusCode.Conflict,
            EmailDispatchFailureCodes.InvalidTransition);
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
    }

    [Test]
    public async Task StatusIsTenantBoundLimitedAndSanitizedAndReplayCannotSelectForeignRow()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid first = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        await factory.SeedDispatchAsync(EmailDispatchStatus.DeadLettered);
        Guid foreign = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked, tenantId: factory.OtherTenantId);
        using var client = factory.Client(factory.TenantAdminId);
        using var response = await client.GetAsync(Root + $"/status?tenantId={PlatformDefaults.DefaultTenantId}&limit=1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        // The existing status action serializes the assembler Task as a result envelope.
        // This dispatch-only migration preserves that wire shape.
        var items = document.RootElement.GetProperty("result").GetProperty("_embedded").GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(1);
        await Assert.That(items[0].GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(items[0].GetProperty("_links").TryGetProperty("replay", out _)).IsTrue();
        foreach (string canary in new[] { "private-recipient", "Private subject canary", "Private body canary", "private-provider-canary", "private-error-canary" })
            await Assert.That(json).DoesNotContain(canary);
        await ProblemAsync(await client.PostAsync(ReplayUrl(foreign), null), HttpStatusCode.NotFound, EmailDispatchFailureCodes.NotFound);
        await ProblemAsync(await client.PostAsync(ReplayUrl(first, factory.OtherTenantId), null), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.GetAsync(Root + $"/status?tenantId={factory.OtherTenantId}"), HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnonymousAndMemberAreDeniedAndTenantAdminCannotControlInstance()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        using var anonymous = factory.CreateClient();
        using var member = factory.Client(factory.MemberId);
        using var tenantAdmin = factory.Client(factory.TenantAdminId);
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, Root + $"/status?tenantId={PlatformDefaults.DefaultTenantId}"),
            (HttpMethod.Post, ReplayUrl(id)), (HttpMethod.Get, Root + "/control"),
            (HttpMethod.Put, Root + "/control/pause?reason=maintenance"),
            (HttpMethod.Put, Root + "/control/rate-limit?rateLimitPerMinute=25")
        })
        {
            using (var unauthenticated = new HttpRequestMessage(method, path))
                await ProblemAsync(await anonymous.SendAsync(unauthenticated), HttpStatusCode.Unauthorized);
            using (var unauthorized = new HttpRequestMessage(method, path))
                await ProblemAsync(await member.SendAsync(unauthorized), HttpStatusCode.Forbidden);
            if (path.Contains("/control", StringComparison.Ordinal))
            {
                using var instanceDenied = new HttpRequestMessage(method, path);
                await ProblemAsync(await tenantAdmin.SendAsync(instanceDenied), HttpStatusCode.Forbidden);
            }
        }
        await Assert.That((await StatusAsync(factory)).Single().DeliveryStatus).IsEqualTo(EmailDispatchStatus.Parked);
        using var admin = factory.Client();
        await Assert.That((await ControlAsync(admin)).IsPaused).IsFalse();
    }

    [Test]
    public async Task NativeValidationRetainsFailureCodesWithoutTransportValidation()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        using var scope = AdminScope(factory);
        var services = scope.ServiceProvider;
        var invalidPause = await services.GetRequiredService<ICommandHandler<SetEmailDispatchProcessorPauseStateCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { IsPaused = true, PauseReason = new string('x', 501) }, default);
        await Assert.That(invalidPause.FailureCode).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
        var invalidRate = await services.GetRequiredService<ICommandHandler<SetEmailDispatchGlobalRateLimitOverrideCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { RateLimitPerMinute = 0 }, default);
        await Assert.That(invalidRate.FailureCode).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
        var invalidReplay = await services.GetRequiredService<ICommandHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = Guid.Empty }, default);
        await Assert.That(invalidReplay.IsSuccess).IsFalse();
        await Assert.That(invalidReplay.Errors).IsNotEmpty();
        await Assert.That(invalidReplay.FailureCode).IsNull();
        var invalidStatus = await services.GetRequiredService<IQueryHandler<GetEmailDispatchStatusQuery, BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>>>()
            .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, Limit = 201 }, default);
        await Assert.That(invalidStatus.IsSuccess).IsFalse();
        await Assert.That(invalidStatus.Errors).IsNotEmpty();
        await Assert.That(invalidStatus.FailureCode).IsNull();
        var control = await services.GetRequiredService<IQueryHandler<GetEmailDispatchProcessorControlQuery, EmailDispatchProcessorControlDto>>()
            .QueryAsync(new(), default);
        await Assert.That(control.IsPaused).IsFalse();
        await Assert.That(control.GlobalSmtpRateLimitPerMinuteOverride).IsNull();
    }

    [Test]
    public async Task ReceiptWriteFailureRollsBackReplayAndAllowsSubsequentRetry()
    {
        var fault = new ReceiptFailure();
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(fault);
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        var before = (await StatusAsync(factory)).Single();
        using (var scope = AdminScope(factory))
        {
            fault.Armed = true;
            await Assert.That(async () => await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = id }, default))
                .Throws<IOException>();
        }
        await Assert.That(fault.Triggered).IsTrue();
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
        using var client = factory.Client();
        await CommandAsync(await client.PostAsync(ReplayUrl(id), null));
        await Assert.That((await StatusAsync(factory)).Single().DeliveryStatus).IsEqualTo(EmailDispatchStatus.Pending);
    }

    [Test]
    public async Task ReplayLosesToCommittedResolutionWithoutResurrectingTheRow()
    {
        var boundary = new TransactionBoundary();
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(boundary);
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        using var client = factory.Client();
        boundary.BeforeStart = async () => await CommandAsync(await client.PostAsync(
            Root + $"/tenants/{PlatformDefaults.DefaultTenantId}/outbox/{id}/resolve-without-replay?reason=reviewed", null));
        await ProblemAsync(await client.PostAsync(ReplayUrl(id), null), HttpStatusCode.Conflict,
            EmailDispatchFailureCodes.ConcurrentTransition);
        await Assert.That((await StatusAsync(factory)).Single().DeliveryStatus).IsEqualTo(EmailDispatchStatus.Skipped);
    }

    [Test]
    public async Task InterleavedProcessorWritesDoNotOverwriteEachOthersControlFields()
    {
        var boundary = new TransactionBoundary();
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(boundary);
        using var client = factory.Client();
        boundary.BeforeStart = async () => await CommandAsync(await client.PutAsync(
            Root + "/control/rate-limit?rateLimitPerMinute=42", null));
        await CommandAsync(await client.PutAsync(Root + "/control/pause?reason=maintenance", null));
        var control = await ControlAsync(client);
        await Assert.That(control.IsPaused).IsTrue();
        await Assert.That(control.GlobalSmtpRateLimitPerMinuteOverride).IsEqualTo(42);
    }

    [Test]
    public async Task CancellationAfterOutboxUpdateRollsBackBeforeReceiptSettlement()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var boundary = new ReceiptFailure
        {
            BeforeWrite = async token =>
            {
                entered.SetResult(token);
                await release.Task.WaitAsync(token);
            }
        };
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(boundary);
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        var before = (await StatusAsync(factory)).Single();
        using (var scope = AdminScope(factory))
        using (var cancellation = new CancellationTokenSource())
        {
            boundary.Armed = true;
            var pending = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = id }, cancellation.Token);
            try
            {
                await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(cancellation.Token);
                await cancellation.CancelAsync();
                await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
            }
            finally { release.TrySetResult(); }
        }
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
    }

    private static IServiceScope AdminScope(NativeEmailDispatchWebApplicationFactory factory)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("internal_user_id", factory.AdminId.ToString())], "Test"))
        };
        return scope;
    }

    private static async Task<IReadOnlyList<EmailDispatchStatusDto>> StatusAsync(NativeEmailDispatchWebApplicationFactory factory)
    {
        using var scope = AdminScope(factory);
        var result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetEmailDispatchStatusQuery, BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>>>()
            .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        return result.Id!;
    }

    private static string ReplayUrl(Guid id, Guid? tenantId = null) =>
        Root + $"/tenants/{tenantId ?? PlatformDefaults.DefaultTenantId}/outbox/{id}/replay";

    private static async Task<EmailDispatchProcessorControlDto> ControlAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Root + "/control");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<EmailDispatchProcessorControlDto>())!;
    }

    private static async Task<BaseCommandResponse<Guid>> CommandAsync(HttpResponseMessage response)
    {
        using (response)
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var result = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            await Assert.That(result.IsSuccess).IsTrue();
            return result;
        }
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        using (response)
        {
            await Assert.That(response.StatusCode).IsEqualTo(status);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
            if (code is not null) await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
        }
    }

    private sealed class ReceiptFailure : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public Func<CancellationToken, Task> BeforeWrite { get; init; } = _ => throw new IOException("Injected receipt-store write failure.");
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("UPDATE ", StringComparison.Ordinal)
                && command.CommandText.Contains("\"ie_email_dispatch_receipts\"", StringComparison.Ordinal))
            {
                Armed = false;
                Triggered = true;
                await BeforeWrite(cancellationToken);
            }
            return result;
        }
    }

    private sealed class TransactionBoundary : DbTransactionInterceptor
    {
        public Func<Task>? BeforeStart { get; set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var action = BeforeStart;
            BeforeStart = null;
            if (action is not null) await action();
            return result;
        }
    }
}
