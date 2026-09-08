using System.Net;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests;

public sealed class ParticipantReadinessBffTests
{
    [Test]
    public async Task CapabilityReadIsForwardedWithoutEchoOrTenantSpoof()
    {
        ReadinessScope scope = ReadinessScope.Create();
        string capability = Guid.CreateVersion7()
            .ToString("N");
        IParticipantReadinessClient api = SuccessfulApi(scope);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            scope.Path);
        request.Headers.Add(
            "X-Registration-Order-Capability",
            capability);
        request.Headers.Add(
            "X-Tenant-Id",
            Guid.CreateVersion7().ToString("D"));

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string payload =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoStore(response);
        await Assert.That(payload).DoesNotContain(capability);
        await api.Received(1)
            .GetParticipantReadinessAsync(
                scope.EventId,
                scope.OrderId,
                scope.ParticipantId,
                scope.AssignmentId,
                capability,
                null,
                null,
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReadinessWriteRequiresAntiforgeryBeforeApiCall()
    {
        ReadinessScope scope = ReadinessScope.Create();
        IParticipantReadinessClient api = SuccessfulApi(scope);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{scope.Path}/complete");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7()));

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.BadRequest);
        await api.DidNotReceiveWithAnyArgs()
            .CompleteParticipantReadinessAsync(
                default,
                default,
                default,
                default,
                default,
                default,
                default);
    }

    [Test]
    public async Task ReadinessWriteRequiresCookieAuthority()
    {
        ReadinessScope scope = ReadinessScope.Create();
        IParticipantReadinessClient api = SuccessfulApi(scope);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        BrowserSession session =
            await IssueBrowserSessionAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{scope.Path}/complete");
        AddBrowserSession(request, session);

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateNoStore(response);
        await api.DidNotReceiveWithAnyArgs()
            .CompleteParticipantReadinessAsync(
                default,
                default,
                default,
                default,
                default,
                default,
                default);
    }

    [Test]
    public async Task AuthenticatedActionsUseGeneratedClientOnly()
    {
        ReadinessScope scope = ReadinessScope.Create();
        IParticipantReadinessClient api = SuccessfulApi(scope);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        string authentication =
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7());
        BrowserSession session =
            await IssueBrowserSessionAsync(
                client,
                authentication);

        foreach (string action in new[]
                 {
                     "complete",
                     "approve",
                     "revoke",
                 })
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{scope.Path}/{action}");
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                authentication);
            request.Headers.Add(
                "X-Tenant-Id",
                Guid.CreateVersion7().ToString("D"));
            AddBrowserSession(request, session);

            using HttpResponseMessage response =
                await client.SendAsync(request);

            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateNoStore(response);
        }

        await api.Received(1)
            .CompleteParticipantReadinessAsync(
                scope.EventId,
                scope.OrderId,
                scope.ParticipantId,
                scope.AssignmentId,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .ApproveParticipantReadinessAsync(
                scope.EventId,
                scope.OrderId,
                scope.ParticipantId,
                scope.AssignmentId,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .RevokeParticipantReadinessAsync(
                scope.EventId,
                scope.OrderId,
                scope.ParticipantId,
                scope.AssignmentId,
                null,
                null,
                Arg.Any<CancellationToken>());
    }

    private static IParticipantReadinessClient SuccessfulApi(
        ReadinessScope scope)
    {
        IParticipantReadinessClient api =
            Substitute.For<IParticipantReadinessClient>();
        var response =
            new HalResourceOfParticipantReadinessDto
            {
                RegistrationTicketAssignmentId =
                    scope.AssignmentId,
                StatusCode = "participant_completion_pending",
                SupportCode = "action_required",
                ActiveAdmissionAvailable = false,
            };
        api.GetParticipantReadinessAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        api.CompleteParticipantReadinessAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        api.ApproveParticipantReadinessAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        api.RevokeParticipantReadinessAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return api;
    }

    private static async Task<BrowserSession>
        IssueBrowserSessionAsync(
            HttpClient client,
            string? authentication = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/status");
        if (authentication is not null)
        {
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                authentication);
        }

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string[] cookies =
            response.Headers.GetValues("Set-Cookie").ToArray();
        string antiforgery = cookies.First(value =>
            value.StartsWith(
                "XSRF-TOKEN=",
                StringComparison.Ordinal));
        int end = antiforgery.IndexOf(';');
        string token = Uri.UnescapeDataString(
            antiforgery["XSRF-TOKEN=".Length..end]);
        string cookieHeader = string.Join(
            "; ",
            cookies.Select(value =>
                value.Split(';', 2)[0]));
        return new BrowserSession(token, cookieHeader);
    }

    private static void AddBrowserSession(
        HttpRequestMessage request,
        BrowserSession session)
    {
        request.Headers.Add(
            "Cookie",
            session.CookieHeader);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            session.AntiforgeryToken);
    }

    private static async Task AssertPrivateNoStore(
        HttpResponseMessage response)
    {
        await Assert.That(
                response.Headers.CacheControl?.Private)
            .IsTrue();
        await Assert.That(
                response.Headers.CacheControl?.NoStore)
            .IsTrue();
        await Assert.That(response.Headers.TryGetValues(
                "Referrer-Policy",
                out IEnumerable<string>? values))
            .IsTrue();
        await Assert.That(values).Contains("no-referrer");
    }

    private static WebApplicationFactory<Program>
        CreateFactory(IParticipantReadinessClient api) =>
        new BlazorBffWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IParticipantReadinessClient>();
                    services.AddSingleton(api);
                });
            });

    private sealed record BrowserSession(
        string AntiforgeryToken,
        string CookieHeader);

    private sealed record ReadinessScope(
        Guid EventId,
        Guid OrderId,
        Guid ParticipantId,
        Guid AssignmentId)
    {
        public string Path =>
            $"/bff/events/{EventId}/participant-readiness/" +
            $"registration-orders/{OrderId}/participants/" +
            $"{ParticipantId}/assignments/{AssignmentId}";

        public static ReadinessScope Create() =>
            new(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
    }
}
