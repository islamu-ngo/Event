using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services.Http;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services.Http;

public sealed class BffClientCheckoutTests
{
    [Test]
    public async Task IssueCheckoutUsesBrowserFetchWithHeaderOnlyCapability()
    {
        var module = new RecordingModule();
        var runtime = new RecordingRuntime(module);
        using var http = new HttpClient(new RejectingHandler()) { BaseAddress = new Uri("https://event.example/") };
        await using var client = new BffClient(http, runtime);

        BffRegistrationPaymentCheckoutTicketResponseDto? response = await client.IssueRegistrationPaymentCheckoutTicketAsync(
            "/bff/registration-payments/events/event/orders/order/checkout-ticket",
            "guest-capability");

        await Assert.That(response?.CheckoutPath).IsEqualTo("/bff/registration-payments/checkout");
        await Assert.That(module.Identifier).IsEqualTo("issueRegistrationPaymentCheckoutTicket");
        await Assert.That(module.Arguments.Length).IsEqualTo(3);
        await Assert.That(module.Arguments[0]?.ToString())
            .IsEqualTo("/bff/registration-payments/events/event/orders/order/checkout-ticket");
        await Assert.That(module.Arguments[1]?.ToString()).IsEqualTo("guest-capability");
        await Assert.That(Guid.TryParse(module.Arguments[2]?.ToString(), out _)).IsTrue();
    }

    [Test]
    public async Task NewCheckoutIssueAbortsPreviousOperation()
    {
        var module = new RacingModule();
        var runtime = new RecordingRuntime(module);
        using var http = new HttpClient(new RejectingHandler()) { BaseAddress = new Uri("https://event.example/") };
        await using var client = new BffClient(http, runtime);

        Task<BffRegistrationPaymentCheckoutTicketResponseDto?> first = client.IssueRegistrationPaymentCheckoutTicketAsync(
            "/bff/registration-payments/events/first/orders/first/checkout-ticket",
            null);
        await module.FirstStarted.Task;
        Task<BffRegistrationPaymentCheckoutTicketResponseDto?> second = client.IssueRegistrationPaymentCheckoutTicketAsync(
            "/bff/registration-payments/events/second/orders/second/checkout-ticket",
            null);

        await Assert.That(await first).IsNull();
        await Assert.That((await second)?.CheckoutPath).IsEqualTo("/bff/registration-payments/checkout");
        await Assert.That(module.AbortedOperationIds).Contains(module.FirstOperationId!);
    }

    private sealed class RecordingRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            ValueTask.FromResult((TValue)module);
    }

    private sealed class RecordingModule : IJSObjectReference
    {
        public string? Identifier { get; private set; }
        public object?[] Arguments { get; private set; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Identifier = identifier;
            Arguments = args ?? [];
            return ValueTask.FromResult((TValue)(object)new BffRegistrationPaymentCheckoutTicketResponseDto(
                "/bff/registration-payments/checkout"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RacingModule : IJSObjectReference
    {
        private readonly TaskCompletionSource<object?> _firstCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _issueCount;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? FirstOperationId { get; private set; }
        public List<string> AbortedOperationIds { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "abortRegistrationPaymentCheckoutTicket")
            {
                string operationId = args![0]!.ToString()!;
                AbortedOperationIds.Add(operationId);
                if (operationId == FirstOperationId)
                {
                    _firstCompletion.TrySetResult(null);
                }
                return ValueTask.FromResult(default(TValue)!);
            }

            string issueOperationId = args![2]!.ToString()!;
            if (Interlocked.Increment(ref _issueCount) == 1)
            {
                FirstOperationId = issueOperationId;
                FirstStarted.TrySetResult();
                return new ValueTask<TValue>(AwaitFirstAsync<TValue>());
            }

            return ValueTask.FromResult((TValue)(object)new BffRegistrationPaymentCheckoutTicketResponseDto(
                "/bff/registration-payments/checkout"));
        }

        private async Task<TValue> AwaitFirstAsync<TValue>()
        {
            object? value = await _firstCompletion.Task;
            return value is null ? default! : (TValue)value;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Checkout issue must execute in the browser.");
    }
}
