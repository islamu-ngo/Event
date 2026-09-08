// ABOUTME: Composes real native Identity lifecycle, relational SMTP policy and global admission for delivery tests.
// ABOUTME: Reuses supervised native provisioning; controls only SMTP, time and explicit concurrency signals.

using Event.Persistence.IntegrationTests.Identity;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Infrastructure;
using Explore.Infrastructure.Authentication;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class LocalLifecycleDeliveryFixture : IAsyncDisposable
{
    internal LocalCredentialFirstUseTests.Fixture Native { get; }
    internal ControlledSmtp Smtp { get; } = new();
    internal bool UseRealSmtp { get; set; }
    internal CancellationToken CancellationToken => Native.CancellationToken;
    private LocalLifecycleDeliveryFixture(LocalCredentialFirstUseTests.Fixture native) => Native = native;

    internal static async Task<LocalLifecycleDeliveryFixture> CreateAsync(IdentityDatabaseTopology topology, bool verified = true)
    {
        var native = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        try
        {
            await native.IssueReadySessionAsync(verified);
            await using (var scope = native.Provider.CreateAsyncScope())
            {
                await EmailDispatchSqliteFixture.ApplyEmailSettingsAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
                    [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "true"),
                     new(null, GovernanceSettingKeys.Email.SmtpHost, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"smtp.instance.test\""),
                     new(null, GovernanceSettingKeys.Email.FromAddress, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"events@instance.test\"")],
                    cancellationToken: native.CancellationToken);
            }
        }
        catch { await native.DisposeAsync(); throw; }
        return new LocalLifecycleDeliveryFixture(native);
    }

    internal Scope Open(Func<string, CancellationToken, Task>? beforeLock = null) => new(this, beforeLock);

    internal sealed class Scope : IAsyncDisposable, ITenantContext
    {
        private readonly AsyncServiceScope _scope;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        internal ExploreDbContext Application { get; }
        internal DbContext Identity { get; }
        internal LocalIdentityLifecycleStore Lifecycle { get; }
        internal LocalIdentityLifecycleDeliveryStore Deliveries { get; }
        internal RelationalSettingMutationLock MutationLock { get; }
        internal LocalIdentityLifecycleDeliveryProcessor Processor { get; }
        internal DefaultAccountAuthorityLifecycleEmailService Router { get; }
        public Guid TenantId { get; } = Guid.Empty;

        internal Scope(LocalLifecycleDeliveryFixture fixture, Func<string, CancellationToken, Task>? beforeLock)
        {
            _scope = fixture.Native.Provider.CreateAsyncScope();
            Application = _scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            Application.TenantContext = this;
            Identity = fixture.Native.Identity(_scope);
            var states = fixture.Native.StateStore(_scope);
            Lifecycle = new(Identity, Application, _scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Native.Clock, states);
            Deliveries = new(Identity, Application, states, fixture.Native.Clock);
            MutationLock = new(Application, new EfCoreUnitOfWork(Application), beforeLock);
            var writer = EmailDispatchSqliteFixture.CreateEmailSettingsWriter(Application, MutationLock);
            var settings = new HierarchicalSettingsResolver(new SystemSettingRepository(Application, MutationLock),
                new TenantSettingRepository(Application, MutationLock), new OrganizationSettingRepository(Application),
                new GroupSettingRepository(Application), new GroupTenantRepository(Application), new UserPreferenceRepository(Application),
                this, MutationLock, _cache, NullLogger<HierarchicalSettingsResolver>.Instance, writer);
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.ArgAt<Guid?>(1) is not null) throw new InvalidOperationException("Lifecycle requested tenant credentials.");
                return SecretResolutionResult.Unconfigured;
            });
            var capabilities = new EmailDeliveryCapabilityResolver(settings, secrets, new SecretBindingRepository(Application));
            ILocalIdentityLifecycleSmtpTransport transport = fixture.UseRealSmtp
                ? new LocalIdentityLifecycleSmtpTransport(new SmtpEmailService(new SmtpConfigResolver(capabilities, this, settings),
                    NullLogger<SmtpEmailService>.Instance)) : fixture.Smtp;
            Processor = new(Lifecycle, Deliveries, MutationLock, settings, capabilities, transport,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://instance.test" }).Build(),
                Options.Create(new EmailDispatchProcessorSettings()));
            Router = new(new UserExternalLoginRepository(Application), [new LocalIdentityLifecycleEmailService(Lifecycle, Deliveries)]);
        }

        public async ValueTask DisposeAsync() { _cache.Dispose(); await _scope.DisposeAsync(); }
    }

    internal sealed class ControlledSmtp : ILocalIdentityLifecycleSmtpTransport
    {
        internal List<LocalIdentityLifecycleTransport> Handoffs { get; } = [];
        internal SmtpDeliveryOutcome Outcome { get; set; } = SmtpDeliveryOutcome.Accepted;
        internal Func<LocalIdentityLifecycleTransport, Guid, CancellationToken, Task>? OnSend { get; set; }
        public async Task<EmailResult> SendAsync(LocalIdentityLifecycleTransport handoff, Guid attemptId,
            Uri callbackUri, SmtpConfiguration configuration, CancellationToken cancellationToken)
        {
            if (configuration.Host != "smtp.instance.test") throw new InvalidOperationException("Incorrect transport ownership.");
            Handoffs.Add(handoff);
            if (OnSend is not null) await OnSend(handoff, attemptId, cancellationToken);
            return Outcome == SmtpDeliveryOutcome.Accepted ? EmailResult.Ok() : EmailResult.Fail("controlled", outcome: Outcome);
        }
    }

    public ValueTask DisposeAsync() => Native.DisposeAsync();
}
