
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using NSubstitute.Extensions;

namespace Explore.Tests.Shared.Settings;

internal sealed class SmtpSettingsDatabase : IAsyncDisposable, ITenantContext
{
    private readonly SqliteConnection _connection;
    private readonly ExploreDbContext _context;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SystemSettingRepository _systemSettings;
    private readonly TenantSettingRepository _tenantSettings;
    private readonly SecretBindingRepository _bindings;
    private readonly Dictionary<Guid, string> _bindingKeys = [];
    private readonly InternalEmailDeliverySettingsWriter _setupWriter;

    private SmtpSettingsDatabase(SqliteConnection connection, ExploreDbContext context)
    {
        _connection = connection;
        _context = context;
        MutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        _systemSettings = SystemSettingRepository.CreateReadOnly(context, MutationLock);
        _tenantSettings = TenantSettingRepository.CreateReadOnly(context, MutationLock);
        _bindings = new SecretBindingRepository(context);
        _setupWriter = new InternalEmailDeliverySettingsWriter(context, MutationLock, new EfCoreUnitOfWork(context));
        Settings = new HierarchicalSettingsReader(_systemSettings, _tenantSettings,
            new OrganizationSettingRepository(context), new GroupSettingRepository(context),
            UserPreferenceRepository.CreateReadOnly(context), _cache);
        Capabilities = new EmailDeliveryCapabilityResolver(Settings, Secrets, _bindings);
        Smtp = new SmtpConfigResolver(Capabilities, this, Settings);
        Secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        Secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
    }

    public Guid TenantId { get; } = Guid.CreateVersion7();
    public Guid ActorId { get; } = Guid.CreateVersion7();
    public ExploreDbContext Context => _context;
    public ISettingMutationLock MutationLock { get; }
    public ISecretResolver Secrets { get; } = Substitute.For<ISecretResolver>();
    public IHierarchicalSettingsReader Settings { get; }
    public EmailDeliveryCapabilityResolver Capabilities { get; }
    public ISmtpConfigResolver Smtp { get; }

    public static async Task<SmtpSettingsDatabase> CreateAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"smtp-settings-{Guid.CreateVersion7():N}.db");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = databasePath
        });
        var context = new ExploreDbContext(options.UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        var fixture = new SmtpSettingsDatabase(connection, context);
        context.AddRange(
            new User
            {
                Id = fixture.ActorId,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = $"smtp-actor-{fixture.ActorId:N}@example.test", FirstName = "SMTP", LastName = "Operator" }
            },
            new Tenant
            {
                Id = fixture.TenantId,
                FullName = "SMTP test tenant",
                Slug = "smtp-test",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Deployment.Mode, "MultiTenant");
        return fixture;
    }

    public async Task ConfigureInstanceAsync()
    {
        await SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        await SetInstanceAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.instance.test");
        await SetInstanceAsync(GovernanceSettingKeys.Email.FromAddress, "events@instance.test");
        await SetInstanceAsync(GovernanceSettingKeys.Email.SmtpSecurity, "StartTls");
        await SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, 587);
    }

    public async Task ConfigureTenantAsync()
    {
        await SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await SetTenantAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.tenant.test");
        await SetTenantAsync(GovernanceSettingKeys.Email.FromAddress, "events@tenant.test");
    }

    public async Task SetInstanceAsync<T>(string key, T value)
    {
        if (EmailDeliverySettingKeys.Contains(key))
            await SetEmailValueAsync(tenantId: null, key, SettingValueSerializer.Serialize(value));
        else
            await SetHumanValueAsync(key, value, tenantId: null);
        Settings.InvalidateCache(SettingScope.Instance);
    }

    public async Task SetTenantAsync<T>(string key, T value)
    {
        if (EmailDeliverySettingKeys.Contains(key))
            await SetEmailValueAsync(TenantId, key, SettingValueSerializer.Serialize(value));
        else
            await SetHumanValueAsync(key, value, TenantId);
        Settings.InvalidateCache(SettingScope.Tenant, TenantId);
    }

    private async Task SetHumanValueAsync<T>(string key, T value, Guid? tenantId)
    {
        string stored = SettingValueSerializer.Serialize(value);
        string input = value is string text ? text : stored;
        var options = (DbContextOptions<ExploreDbContext>)_context.GetService<IDbContextOptions>();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
            () => new ExploreDbContext(options));
        await authenticated.RunAsync(tenantId, session => session.ValueAsync(key, input, stored, async token =>
        {
            if (tenantId is { } tenant)
                await new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .SetValueAsync(tenant, key, stored, token, actorId: session.ActorId);
            else
            {
                var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
                var setting = await repository.GetByKey(key, token) ?? new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = stored,
                    ValueType = SettingValueType.String,
                    CreatedBy = session.ActorId,
                    CreatedAt = DateTime.UtcNow
                };
                setting.Value = stored;
                setting.UpdatedBy = session.ActorId;
                await repository.UpsertAsync(setting, token);
            }
            return true;
        }));
    }

    /// <summary>Seeds corrupt persisted state for reader fail-closed tests, not a lawful setting mutation.</summary>
    public async Task SeedCorruptInstanceValueAsync(string key, string storedValue)
    {
        var setting = await _context.SystemSettings.SingleAsync(row => row.SettingKey == key);
        setting.Value = storedValue;
        await _context.SaveChangesAsync();
        Settings.InvalidateCache(SettingScope.Instance);
    }

    // Nonhuman fixture setup deliberately leaves the reader cache intact for freshness tests.
    public async Task SetEmailValueAsync(Guid? tenantId, string key, string value)
    {
        var result = key == GovernanceSettingKeys.Email.DeliveryEnabled && value == "false"
            ? await _setupWriter.DisableInternalAsync(tenantId, actorUserId: null)
            : await _setupWriter.ApplyInternalAsync([new(TenantId: tenantId, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetValue, Value: value)], actorUserId: null);
        if (result.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
            throw new InvalidOperationException($"SMTP fixture mutation rejected: {result.Status}.");
    }

    public async Task RemoveEmailValueAsync(Guid? tenantId, string key)
    {
        var result = await _setupWriter.ApplyInternalAsync(
            [new(tenantId, key, EmailDeliverySettingMutationKind.Remove)], actorUserId: null);
        result.EnsureAccepted();
    }

    public async Task AddCredentialsAsync(SecretScope scope)
    {
        await AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Username, scope);
        await AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Password, scope);
    }

    public async Task AddBindingAsync(string key, SecretScope scope)
    {
        var binding = SecretBinding.CreateEnvironmentVariable(key, scope,
            scope == SecretScope.Tenant ? TenantId : null, $"SMTP_TEST_{Guid.CreateVersion7():N}");
        binding.CreatedAt = DateTime.UtcNow;
        await _bindings.Create(binding);
        _bindingKeys.Add(binding.Id, key);
    }

    public (string Username, string Password) ResolveCredentials(SecretScope returnedScope, Guid? returnedTenantId,
        Guid? requestedTenantId = null)
    {
        var username = Guid.CreateVersion7().ToString("N");
        var password = Guid.CreateVersion7().ToString("N");
        var expectedTenantId = requestedTenantId ?? returnedTenantId;
        Secrets.Configure().ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<Guid?>(1) != expectedTenantId)
                    throw new InvalidOperationException("Credentials requested outside transport ownership.");
                var key = call.ArgAt<string>(0);
                return SecretResolutionResult.Resolved(new ResolvedSecret(key,
                    key == SecretDefinitionRegistry.Keys.Smtp.Username ? username : password,
                    SecretSourceType.EnvironmentVariable, returnedScope, returnedTenantId, DateTimeOffset.UtcNow));
            });
        Secrets.Configure().ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<Guid>(0) != expectedTenantId)
                    throw new InvalidOperationException("Credentials requested outside transport ownership.");
                var key = _bindingKeys[call.ArgAt<Guid>(1)];
                return SecretResolutionResult.Resolved(new ResolvedSecret(key,
                    key == SecretDefinitionRegistry.Keys.Smtp.Username ? username : password,
                    SecretSourceType.EnvironmentVariable, returnedScope, returnedTenantId, DateTimeOffset.UtcNow));
            });
        return (username, password);
    }

    public void RejectSecretReads()
    {
        Secrets.Configure().ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns<SecretResolutionResult>(_ => throw new InvalidOperationException("Secret provider must not be called."));
        Secrets.Configure().ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<SecretResolutionResult>(_ => throw new InvalidOperationException("Secret provider must not be called."));
    }

    public async ValueTask DisposeAsync()
    {
        var databasePath = _connection.DataSource;
        _cache.Dispose();
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
        File.Delete(databasePath);
    }
}
