using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class EmailDisableHandlerAuthenticationTests
{
    /// <summary>
    /// Retains an actual authenticated Local request and its credential lifetime. The selected
    /// email owner, preparation, commit authorities, finalizer, handlers, and cache are production
    /// DI services. Only lock/transaction observation and the external audit sink are test seams.
    /// </summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly Scenario scenario;
        private readonly AsyncServiceScope requestScope;
        private readonly AsyncServiceScope selectedScope;
        private readonly DefaultHttpContext request;
        private readonly IHttpContextAccessor accessor;
        private SessionObservations observations = null!;
        internal ExploreDbContext Context { get; private set; } = null!;
        internal TransactionObserver Transactions { get; } = new();
        internal List<string> LeaseKeys => observations.LeaseKeys;
        internal CommittedNotificationObserver Notifications => observations.Notifications;
        internal AuditSink Audit => observations.Audit;
        internal Func<string, CancellationToken, Task>? BeforeLock
        {
            get => observations.BeforeLock;
            set => observations.BeforeLock = value;
        }

        private Session(Scenario scenario)
        {
            this.scenario = scenario;
            requestScope = scenario.Host.Services.CreateAsyncScope();
            selectedScope = scenario.Host.Services.CreateAsyncScope();
            request = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
            accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        }

        internal static void ConfigureServices(IServiceCollection services)
        {
            // The shared Local HTTP fixture installs a runtime context. As in the authentic SMTP
            // fixture, restore execution-lease adoption so every DI collaborator uses this owner.
            services.AddScoped(provider =>
            {
                if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                    return selected;
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                context.TenantContext = provider.GetService<ITenantContext>();
                context.CurrentUserService = provider.GetService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                return context;
            });
            services.AddScoped<SessionObservations>();
            services.AddScoped<ISettingMutationLock>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                var observation = provider.GetRequiredService<SessionObservations>();
                return new RelationalSettingMutationLock(context, provider.GetRequiredService<IUnitOfWork>(),
                    async (key, token) =>
                    {
                        if (context.Database.CurrentTransaction is not null)
                            throw new InvalidOperationException("The full SMTP lease must precede the transaction.");
                        observation.LeaseKeys.Add(key);
                        if (observation.BeforeLock is not null) await observation.BeforeLock(key, token);
                    });
            });
            // Preserve the production notification chain and insert the independent committed
            // observer before it. The actual cache invalidation and audit handlers still execute.
            int firstHandler = services.ToList().FindIndex(descriptor =>
                descriptor.ServiceType == typeof(INotificationHandler<SettingChangedNotification>));
            if (firstHandler < 0)
                throw new InvalidOperationException("The production setting notification chain is required.");
            services.Insert(firstHandler, ServiceDescriptor.Scoped<INotificationHandler<SettingChangedNotification>>(
                provider => provider.GetRequiredService<SessionObservations>().Notifications));
            services.AddScoped<ILogger<SettingAuditLogHandler>>(provider =>
                provider.GetRequiredService<SessionObservations>().Audit);
        }

        internal static async Task<Session> CreateAsync(Scenario scenario, Guid actor, string? accessToken,
            Guid currentTenant, Guid? ownerTenant)
        {
            var session = new Session(scenario);
            try
            {
                await session.InitializeAsync(actor, accessToken, currentTenant, ownerTenant);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        private async Task InitializeAsync(Guid actor, string? accessToken, Guid currentTenant, Guid? ownerTenant)
        {
            requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(currentTenant);
            accessor.HttpContext = request;
            try
            {
                HumanCommitSubject? subject = null;
                HumanAuthenticationLifetime? lifetime = null;
                if (accessToken is not null)
                {
                    request.Request.Headers.Authorization = "Bearer " + accessToken;
                    var authentication = await request.AuthenticateAsync();
                    await Assert.That(authentication.Succeeded).IsTrue();
                    request.User = authentication.Principal!;
                    subject = await HumanCommitSubject.ResolveAsync(request.User,
                        requestScope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
                        CancellationToken.None)
                        ?? throw new InvalidOperationException("The original linked Local account is required.");
                    lifetime = await requestScope.ServiceProvider.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                        .ObserveAsync(request.User, CancellationToken.None)
                        ?? throw new InvalidOperationException("Original validated Local credential custody is required.");
                    await Assert.That(subject.PrincipalId).IsEqualTo(actor);
                    await Assert.That(subject.AccountKey).IsEqualTo(lifetime.AccountKey);
                    await Assert.That(lifetime.LocalSession).IsNotNull();
                    await Assert.That(lifetime.IsValidAt(scenario.Clock.GetUtcNow())).IsTrue();
                }
                else
                {
                    // The missing-actor case is truly anonymous, not a fabricated Guid.Empty claim.
                    await Assert.That(actor).IsEqualTo(Guid.Empty);
                    await Assert.That((await request.AuthenticateAsync()).Succeeded).IsFalse();
                    await Assert.That(request.User.Identity?.IsAuthenticated ?? false).IsFalse();
                }

                var services = selectedScope.ServiceProvider;
                services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(currentTenant);
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                    PrimaryDatabaseConfiguration.Bind(services.GetRequiredService<IConfiguration>(),
                        ownerTenant.HasValue ? PrimaryDatabaseRole.EmailPolicyTenantMutation
                            : PrimaryDatabaseRole.EmailPolicyInstanceMutation));
                options.UseSnakeCaseNamingConvention();
                options.AddInterceptors(Transactions);
                Context = new ExploreDbContext(options.Options)
                {
                    TenantContext = ownerTenant is Guid tenant ? new SelectedTenant(tenant) : null,
                    CurrentUserService = requestScope.ServiceProvider.GetRequiredService<ICurrentUserService>()
                };
                Context.ClearTenantFilterBypass();
                await Context.Database.OpenConnectionAsync();
                var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
                lease.Bind(Context, ownerTenant, PolicyMutationPurpose.EmailDelivery);
                if (subject is not null)
                    lease.BindHuman(subject, ownerTenant.HasValue ? HumanAdministratorCommitPurpose.EmailTenant
                        : HumanAdministratorCommitPurpose.EmailInstance, enrollment: null, lifetime);
                await Assert.That(ReferenceEquals(services.GetRequiredService<ExploreDbContext>(), Context)).IsTrue();
                var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
                await preparation.PrepareAsync(CancellationToken.None);
                await Assert.That(ReferenceEquals(preparation.Subject, subject)).IsTrue();
                observations = services.GetRequiredService<SessionObservations>();
                if (subject is not null)
                {
                    _ = services.GetRequiredService<IHumanActionCommitFinalizer>();
                    _ = services.GetRequiredService<IInstanceAdministratorCommitAuthority>();
                    _ = services.GetRequiredService<ITenantAdministratorCommitAuthority>();
                }
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }

        // Resolve the concrete native handlers from production DI on the exact preselected owner.
        // Transport scope decorators reject malformed targets before the original handler response
        // contract; these migrated handler tests deliberately retain that native response boundary.
        internal Task<BaseCommandResponse<EmailDeliveryDisablePreviewDto>> PreviewCommandAsync(
            PreviewEmailDeliveryDisableCommand command, CancellationToken cancellationToken) =>
            InOriginalRequestAsync(() => selectedScope.ServiceProvider
                .GetRequiredService<PreviewEmailDeliveryDisableCommandHandler>().ExecuteAsync(command, cancellationToken));

        internal Task<BaseCommandResponse<Guid>> DisableCommandAsync(
            DisableEmailDeliveryCommand command, CancellationToken cancellationToken) =>
            InOriginalRequestAsync(() => selectedScope.ServiceProvider
                .GetRequiredService<DisableEmailDeliveryCommandHandler>().ExecuteAsync(command, cancellationToken));

        private async Task<T> InOriginalRequestAsync<T>(Func<Task<T>> operation)
        {
            HttpContext? previous = accessor.HttpContext;
            accessor.HttpContext = request;
            try { return await operation(); }
            finally { accessor.HttpContext = previous; }
        }

        internal async Task<EmailDeliveryDisablePreviewDto> PreviewAsync(Guid? target)
        {
            var result = await PreviewCommandAsync(new(target), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id!.CanDisable).IsTrue();
            await Assert.That(result.Id.ConfirmationToken).IsNotNull();
            return result.Id;
        }

        internal async Task<string?> FailureAsync(bool preview, DisableEmailDeliveryCommand command, CancellationToken token = default) =>
            preview ? (await PreviewCommandAsync(new(command.TenantId), token)).FailureCode
                : (await DisableCommandAsync(command, token)).FailureCode;

        internal Task<bool> CurrentAdminAsync(bool tenantTarget) => InOriginalRequestAsync(() =>
        {
            var admin = selectedScope.ServiceProvider.GetRequiredService<IAdminContext>();
            return tenantTarget ? admin.IsTenantAdminAsync(scenario.TenantId) : admin.IsInstanceAdminAsync();
        });

        internal Task<bool> CachedSentinelAsync() => InOriginalRequestAsync(() =>
            selectedScope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>().ResolveAsync<bool>(
                GovernanceSettingKeys.EventReporting.IntakeEnabled, new SettingContext()));

        internal async Task PrimeStaleCacheAsync()
        {
            await Assert.That(await CachedSentinelAsync()).IsTrue();
            await using var writer = scenario.CreateIndependentContext();
            await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.EventReporting.IntakeEnabled)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
            await Assert.That(await CachedSentinelAsync()).IsTrue();
        }

        internal async Task AssertNoEffectsAsync()
        {
            await Assert.That(Notifications.Published).IsEmpty();
            await Assert.That(Audit.Entries).IsEmpty();
        }

        internal async Task AssertNoLeaseOrEffectsAsync()
        {
            await Assert.That(LeaseKeys).IsEmpty();
            await Assert.That(Transactions.Started.Task.IsCompleted).IsFalse();
            await AssertNoEffectsAsync();
        }

        internal async Task AssertFullLeaseAsync()
        {
            await Assert.That(LeaseKeys.ToHashSet(StringComparer.Ordinal).SetEquals(EmailDeliverySettingKeys.All)).IsTrue();
            await Assert.That(LeaseKeys[0]).IsEqualTo(GovernanceSettingKeys.Email.DeliveryEnabled);
            await Assert.That(Transactions.Isolations.Count).IsGreaterThan(0);
            await Assert.That(Transactions.Isolations.All(level => level == IsolationLevel.Serializable)).IsTrue();
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await selectedScope.DisposeAsync();
            if (Context is not null) await Context.DisposeAsync();
            await requestScope.DisposeAsync();
        }
    }

    private sealed class SessionObservations(ExploreDbContext context)
    {
        internal List<string> LeaseKeys { get; } = [];
        internal Func<string, CancellationToken, Task>? BeforeLock { get; set; }
        internal CommittedNotificationObserver Notifications { get; } = new(context);
        internal AuditSink Audit { get; } = new(context);
    }

    private sealed class CommittedNotificationObserver(ExploreDbContext context)
        : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        internal Func<SettingChangedNotification, CancellationToken, Task>? OnPublishing { get; set; }

        public async Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must be published after commit.");
            if (OnPublishing is not null) await OnPublishing(notification, cancellationToken);
            Published.Add(notification);
        }
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<IsolationLevel> Isolations { get; } = [];
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            Isolations.Add(result.IsolationLevel);
            Started.TrySetResult();
            return result;
        }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TransactionStarted(connection, eventData, result));
    }

    private sealed class AuditSink(ExploreDbContext context) : ILogger<SettingAuditLogHandler>
    {
        internal List<Dictionary<string, object?>> Entries { get; } = [];
        internal Exception? Failure { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Audit escaped before commit.");
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value));
            if (Failure is not null) throw Failure;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
