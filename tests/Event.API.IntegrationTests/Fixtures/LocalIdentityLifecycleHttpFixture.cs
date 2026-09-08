
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Models;
using Explore.Domain.Constants;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed class LocalIdentityLifecycleHttpFixture : IAsyncDisposable
{
    internal LocalAdmissionWebApplicationFactory Native { get; private set; } = null!;
    internal WebApplicationFactory<Program> Host { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal LocalAuthRequestDto Login { get; private set; } = null!;
    internal LocalIdentityBinding Binding { get; private set; } = null!;
    internal RecordingSmtp Smtp { get; } = new();
    internal LifecycleClock Clock { get; } = new();
    internal static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    internal static async Task<LocalIdentityLifecycleHttpFixture> CreateAsync(
        bool verified = true, bool emailEnabled = true,
        IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated,
        IInterceptor? interceptor = null, bool rateLimiting = false)
    {
        var fixture = new LocalIdentityLifecycleHttpFixture();
        try
        {
            fixture.Native = await LocalAdmissionWebApplicationFactory.CreateAsync(
                identityTopology: topology, persistenceInterceptor: interceptor, enableRateLimiting: rateLimiting);
            await using (ExploreDbContext seed = fixture.Native.CreateDatabase())
            {
                var intent = await seed.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, CancellationToken);
                intent.Value = emailEnabled ? "true" : "false";
                var host = await seed.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost, CancellationToken);
                host.Value = "\"smtp.lifecycle.test\"";
                var from = await seed.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.FromAddress, CancellationToken);
                from.Value = "\"events@lifecycle.test\"";
                await seed.SaveChangesAsync(CancellationToken);
            }
            fixture.Login = await fixture.Native.SeedLocalUserAsync(verified);
            fixture.Host = fixture.Native.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://lifecycle.test" }));
                builder.ConfigureTestServices(services =>
                {
                    // Disable timer scheduling, not the production processor. Tests explicitly drain admitted work
                    // and synchronously observe SMTP completion instead of waiting for a background polling interval.
                    ServiceDescriptor? worker = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType == typeof(LocalIdentityLifecycleDeliveryWorker));
                    if (worker is not null) services.Remove(worker);
                    services.RemoveAll<ILocalIdentityLifecycleSmtpTransport>();
                    services.AddSingleton<ILocalIdentityLifecycleSmtpTransport>(fixture.Smtp);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(fixture.Clock);
                });
            });
            fixture.Client = fixture.Host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            await using DbContext identity = fixture.Native.CreateIdentityDatabase();
            Guid subject = await identity.Set<LocalIdentityUser>().Where(user => user.Email == fixture.Login.Identifier)
                .Select(user => user.Id).SingleAsync(CancellationToken);
            fixture.Binding = (await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .ReadLinkedIdentityAsync(subject, CancellationToken))!;
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal async Task<string> SignInAsync(LocalAuthRequestDto? login = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync("/api/auth/local/login", login ?? Login, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        return body.RootElement.GetProperty("token").GetString()!;
    }

    internal async Task<LocalIdentityLifecycleTransport> DrainOneAsync()
    {
        await using var scope = Host.Services.CreateAsyncScope();
        int before = Smtp.Handoffs.Count;
        await scope.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(Smtp.Handoffs.Count).IsEqualTo(before + 1);
        return Smtp.Handoffs[^1];
    }

    internal async Task<IdentitySnapshot> ReadIdentityAsync()
    {
        await using DbContext database = Native.CreateIdentityDatabase();
        var user = await database.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(row => row.Id == Binding.LocalSubjectId, CancellationToken);
        return new(user.Email, user.EmailConfirmed, user.PasswordHash!, user.SecurityStamp!);
    }

    internal async Task<MirrorSnapshot> ReadMirrorAsync()
    {
        await using ExploreDbContext database = Native.CreateDatabase();
        var user = await database.Users.Include(row => row.Pii).Include(row => row.Actor)
            .SingleAsync(row => row.Id == Binding.LocalSubjectId, CancellationToken);
        var login = await database.UserExternalLogins.SingleAsync(row => row.Id == Binding.ExternalLoginId, CancellationToken);
        return new(user.Email, user.EmailVerified, user.Actor!.Id, login.Id, login.UserId, login.ProviderKey,
            await database.Users.CountAsync(CancellationToken), await database.Actors.CountAsync(CancellationToken),
            await database.UserExternalLogins.CountAsync(CancellationToken));
    }

    internal async Task<HttpResponseMessage> PostAuthorizedAsync<T>(string path, T body, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request, CancellationToken);
    }

    internal static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Host is not null) await Host.DisposeAsync();
        if (Native is not null) await Native.DisposeAsync();
    }

    internal sealed record IdentitySnapshot(string? Email, bool Verified, string PasswordHash, string SecurityStamp);
    internal sealed record MirrorSnapshot(string Email, bool? Verified, Guid ActorId, Guid LoginId, Guid LoginUserId,
        string Subject, int UserCount, int ActorCount, int LoginCount);

    internal sealed class RecordingSmtp : ILocalIdentityLifecycleSmtpTransport
    {
        internal List<LocalIdentityLifecycleTransport> Handoffs { get; } = [];
        public Task<EmailResult> SendAsync(LocalIdentityLifecycleTransport handoff, Guid attemptId,
            Uri callbackUri, SmtpConfiguration configuration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (configuration.Host != "smtp.lifecycle.test" || callbackUri.AbsolutePath != "/auth/local-account-recovery")
                throw new InvalidOperationException("Lifecycle transport did not use the configured global authority.");
            Handoffs.Add(handoff);
            return Task.FromResult(EmailResult.Ok());
        }
    }

    internal sealed class LifecycleClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
