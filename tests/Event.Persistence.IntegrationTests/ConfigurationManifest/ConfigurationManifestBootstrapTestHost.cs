using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using IConfigurationManifestStartupRunner = Explore.Application.Features.ConfigurationManifest.Application.IConfigurationManifestStartupRunner;
using ConfigurationManifestStartupException = Explore.Application.Features.ConfigurationManifest.Application.ConfigurationManifestStartupException;

using Explore.Application.Features.ConfigurationManifest.Ingestion;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

/// <summary>
/// Writes an operator artifact, then invokes the real registered startup port.
/// Observation callbacks never replace the actual issuer, owner or native writer.
/// </summary>
internal sealed class ConfigurationManifestBootstrapTestHost : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string directory;
    private readonly string path;
    internal Func<string, ExploreDbContext, CancellationToken, Task>? BeforeLock { get; set; }
    internal Func<ExploreDbContext, ConfigurationManifestApplyPlan, CancellationToken, Task>? AfterObservation { get; set; }
    internal string? ArtifactDigest { get; private set; }
    internal string ArtifactPath => path;
    internal IServiceProvider Services => host.Services;

    internal ConfigurationManifestBootstrapTestHost(PrimaryDatabaseConnectionOptions target,
        Func<ExploreDbContext> observer, string? migrationLogin = null,
        INotificationHandler<SettingChangedNotification>? consumer = null, IModel? model = null,
        IConfigurationManifestBootstrapSourceAuthority? counterfeitAuthority = null,
        ConfigurationManifestMode mode = ConfigurationManifestMode.Bootstrap,
        ConfigurationManifestEffectDeliveryMode effectDeliveryMode = ConfigurationManifestEffectDeliveryMode.Immediate)
    {
        if (target.Role != PrimaryDatabaseRole.Global)
            throw new ArgumentException("The fixture must explicitly select its real Global target.", nameof(target));
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "agent-tmp", "manifest-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "configuration-manifest.json");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConfigurationManifestBootstrapDeployment.ModeVariable] = mode.ToString(),
            [ConfigurationManifestBootstrapDeployment.PathVariable] = path,
            ["Database:Global:Username"] = target.Username,
            ["Database:Migrator:Username"] = migrationLogin
        }).Build();
        host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(TimeProvider.System);
            services.AddMemoryCache();
            services.AddSingleton<IGlobalExploreDbContextFactory>(new SelectedGlobal(target, model));
            services.AddScoped(_ => observer());
            services.AddConfigurationManifestStartup(configuration, effectDeliveryMode);
            services.AddConfigurationManifestPersistence();
            if (counterfeitAuthority is not null)
            {
                services.RemoveAll<IConfigurationManifestBootstrapSourceAuthority>();
                services.AddSingleton(counterfeitAuthority);
            }
            services.AddScoped<IHierarchicalSettingsReader>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                var locks = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
                return new HierarchicalSettingsReader(SystemSettingRepository.CreateReadOnly(context, locks),
                    TenantSettingRepository.CreateReadOnly(context, locks), new OrganizationSettingRepository(context),
                    new GroupSettingRepository(context), UserPreferenceRepository.CreateReadOnly(context),
                    provider.GetRequiredService<IMemoryCache>());
            });
            services.AddScoped<ITypedSettingsDocumentResolver>(provider =>
                new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(provider.GetRequiredService<ExploreDbContext>()),
                    provider.GetRequiredService<IMemoryCache>()));
            if (consumer is not null) services.AddSingleton(consumer);
            services.RemoveAll<IConfigurationManifestApplier>();
            services.AddScoped<ConfigurationManifestApplier>();
            services.AddScoped<RecordingApplier>();
            services.AddScoped<IConfigurationManifestApplier>(provider => provider.GetRequiredService<RecordingApplier>());
        }).Build();
    }

    internal static ConfigurationManifestBootstrapTestHost ForPostgres(PostgreSqlContainerFixture fixture,
        ConfigurationManifestMode mode = ConfigurationManifestMode.Bootstrap) =>
        new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), () => fixture.CreateDbContext(),
            fixture.NativeQueueEnrollment.MigratorLogin, mode: mode,
            // This lane verifies original Global bootstrap and its durable effect envelope.
            // Runtime claim-bound worker delivery is a separate contract, not simulated here.
            effectDeliveryMode: ConfigurationManifestEffectDeliveryMode.DeferredToRuntime);

    internal static ConfigurationManifestBootstrapTestHost ForSqlite(Func<ExploreDbContext> observer,
        INotificationHandler<SettingChangedNotification>? consumer = null)
    {
        using var context = observer();
        var original = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        string database = original.Mode == SqliteOpenMode.Memory
            ? $"file:{Uri.EscapeDataString(original.DataSource)}?mode=memory&cache=shared" : original.DataSource;
        return new(new PrimaryDatabaseConnectionOptions
        { Role = PrimaryDatabaseRole.Global, Provider = PrimaryDatabaseProvider.Sqlite, Database = database },
            observer, consumer: consumer, model: context.Model);
    }

    internal async Task<BaseCommandResponse<Guid>> ExecuteAsync(ApplyConfigurationManifestCommand request, CancellationToken token)
    {
        // This is operator fixture input, not an authority DTO: the production issuer
        // opens this selected regular file and computes its own original byte digest.
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(request.Source.Manifest,
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);
        ArtifactDigest = Convert.ToHexStringLower(SHA256.HashData(content));
        await File.WriteAllBytesAsync(path, content, token);
        await using var scope = host.Services.CreateAsyncScope();
        var owner = (ConfigurationManifestBootstrapOwnerFactory)scope.ServiceProvider.GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();
        owner.BeforeOriginalLock = BeforeLock;
        owner.AfterOriginalObservation = AfterObservation;
        var recording = scope.ServiceProvider.GetRequiredService<RecordingApplier>();
        try { await scope.ServiceProvider.GetRequiredService<IConfigurationManifestStartupRunner>().RunAsync(token); }
        catch (ConfigurationManifestStartupException exception) when (recording.Result is { IsSuccess: false } failed
            && exception.OperationId == failed.Id && exception.FailureCode == failed.FailureCode)
        {
            return recording.Result!;
        }
        return recording.Result ?? throw new InvalidOperationException("The genuine startup source did not return an operation.");
    }

    public async ValueTask DisposeAsync()
    {
        if (host is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
        else host.Dispose();
        File.Delete(path);
        Directory.Delete(directory);
    }

    private sealed class RecordingApplier(ConfigurationManifestApplier actual) : IConfigurationManifestApplier
    {
        internal BaseCommandResponse<Guid>? Result { get; private set; }
        public async Task<BaseCommandResponse<Guid>?> ApplyOriginalStartupAsync(CancellationToken token) =>
            Result = await actual.ApplyOriginalStartupAsync(token);
        public async Task<BaseCommandResponse<Guid>> ApplyAsync(
            Explore.Application.Features.ConfigurationManifest.Ingestion.ConfigurationManifestReadResult source, CancellationToken token) =>
            Result = await actual.ApplyAsync(source, token);
    }

    private sealed class SelectedGlobal(PrimaryDatabaseConnectionOptions target, IModel? model) : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, target);
            if (model is not null) builder.UseModel(model);
            if (target.Provider == PrimaryDatabaseProvider.PostgreSql)
                builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            var context = new ExploreDbContext(builder.Options);
            context.ClearTenantFilterBypass();
            context.TenantContext = null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            return Task.FromResult(context);
        }
    }
}
