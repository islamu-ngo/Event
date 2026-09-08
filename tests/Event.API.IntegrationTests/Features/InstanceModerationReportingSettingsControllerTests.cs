using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class InstanceModerationReportingSettingsControllerAnonymousTests
{
    private const string LocksPath = "/api/instance/settings/moderation-reporting/locks";
    private readonly ApiTestFixture _fixture;

    public InstanceModerationReportingSettingsControllerAnonymousTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task UpdateLocks_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PatchAsJsonAsync(LocksPath, new UpdateReportingProviderLocksDto());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}

public sealed class InstanceModerationReportingSettingsControllerAuthorizedTests
{
    private const string LocksPath = "/api/instance/settings/moderation-reporting/locks";

    [Test]
    public async Task UpdateLocks_WithAuth_ShouldSendCommand()
    {
        var mediator = new LockMediator(allowUpdate: true);
        using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();
        request.Content = JsonContent.Create(new UpdateReportingProviderLocksDto
        {
            General = new ReportingProviderLockUpdateDto { Locked = false },
            Coop = new ReportingProviderLockUpdateDto { Locked = true }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mediator.LastCommand).IsNotNull();
        await Assert.That(mediator.LastCommand!.Locks.General!.Locked).IsFalse();
        await Assert.That(mediator.LastCommand.Locks.Osprey).IsNull();
        await Assert.That(mediator.LastCommand.Locks.Coop!.Locked).IsTrue();
    }

    [Test]
    public async Task UpdateLocks_WhenCommandDeniesAdmin_ShouldReturnForbidden()
    {
        using var factory = CreateFactory(new LockMediator(allowUpdate: false));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();
        request.Content = JsonContent.Create(new UpdateReportingProviderLocksDto());

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateLocks_WhenFailureMessageIsUnclassified_ShouldReturnBadRequest()
    {
        using var factory = CreateFactory(new LockMediator(
            allowUpdate: false,
            failureMessage: "Moderation reporting provider lock update failed."));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();
        request.Content = JsonContent.Create(new UpdateReportingProviderLocksDto());

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static WebApplicationFactory<Program> CreateFactory(IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, LocksPath);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private sealed class LockMediator(bool allowUpdate, string? failureMessage = null) : IMediator
    {
        public UpdateReportingProviderLocksCommand? LastCommand { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                UpdateReportingProviderLocksCommand command => Update(command),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => request switch
            {
                UpdateReportingProviderLocksCommand command => Task.FromResult<object?>(Update(command)),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private BaseCommandResponse<Guid> Update(UpdateReportingProviderLocksCommand command)
        {
            LastCommand = command;

            return allowUpdate
                ? BaseCommandResponse.Success(Guid.Empty, "Updated")
                : failureMessage is null
                    ? BaseCommandResponse.Authorization<Guid>("Only instance administrators can update moderation reporting provider locks.")
                    : BaseCommandResponse.Validation<Guid>([failureMessage], failureMessage);
        }
    }
}
