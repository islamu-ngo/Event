
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
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
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
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
    private readonly ServiceProvider _provider = new ServiceCollection().BuildServiceProvider();
    private readonly EmailDeliveryDisableTokenService _tokens = new(new EphemeralDataProtectionProvider());

    private SmtpSettingsDatabase(SqliteConnection connection, ExploreDbContext context)
    {
        _connection = connection;
        _context = context;
        MutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        _systemSettings = new SystemSettingRepository(context, MutationLock);
        _tenantSettings = new TenantSettingRepository(context, MutationLock);
        _bindings = new SecretBindingRepository(context);
        Writer = new EmailDeliverySettingsWriter(context, MutationLock, new EfCoreUnitOfWork(context),
            new EmailDeliveryDisableImpactReader(context), _tokens);
        Settings = new HierarchicalSettingsResolver(_systemSettings, _tenantSettings,
            new OrganizationSettingRepository(context), new GroupSettingRepository(context),
            new GroupTenantRepository(context), new UserPreferenceRepository(context), this,
            MutationLock, _cache, NullLogger<HierarchicalSettingsResolver>.Instance, Writer);
        Capabilities = new EmailDeliveryCapabilityResolver(Settings, Secrets, _bindings);
        Smtp = new SmtpConfigResolver(Capabilities, this, Settings);
        InstanceSmtp = new InstanceSmtpSettingService(_systemSettings, Writer, new Mediator(_provider));
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
    public IHierarchicalSettingsResolver Settings { get; }
    public EmailDeliveryCapabilityResolver Capabilities { get; }
    public ISmtpConfigResolver Smtp { get; }
    public InstanceSmtpSettingService InstanceSmtp { get; }
    public EmailDeliverySettingsWriter Writer { get; }

    public static async Task<SmtpSettingsDatabase> CreateAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"smtp-settings-{Guid.CreateVersion7():N}.db");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = databasePath
        });
        var context = new ExploreDbContext(options.UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await context.Database.EnsureCreatedAsync();
        var fixture = new SmtpSettingsDatabase(connection, context);
        context.AddRange(
            new SettingValueTypeLookup { Id = 0, MasterCode = "String", FullName = "String" },
            new SettingValueTypeLookup { Id = 1, MasterCode = "Integer", FullName = "Integer" },
            new SettingValueTypeLookup { Id = 2, MasterCode = "Boolean", FullName = "Boolean" },
            new SettingScopeLookup { Id = 1, MasterCode = "Instance", FullName = "Instance" },
            new SettingScopeLookup { Id = 2, MasterCode = "Tenant", FullName = "Tenant" },
            new SecretSourceTypeLookup { Id = (int)SecretSourceType.EnvironmentVariable, MasterCode = "EnvironmentVariable", FullName = "Environment variable" },
            new SecretValidationStatus { Id = (int)SecretValidationResult.NotValidated, MasterCode = "NotValidated", FullName = "Not validated" },
            new User
            {
                Id = fixture.ActorId, CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = $"smtp-actor-{fixture.ActorId:N}@example.test", FirstName = "SMTP", LastName = "Operator" }
            },
            new Tenant
            {
                Id = fixture.TenantId, FullName = "SMTP test tenant", Slug = "smtp-test",
                TenantStatus = new TenantStatus
                {
                    Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true
                },
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
            await _systemSettings.UpsertAsync(new SystemSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = key, Value = SettingValueSerializer.Serialize(value),
                ValueType = SettingValueType.String, CreatedAt = DateTime.UtcNow
            });
        Settings.InvalidateCache(SettingScope.Instance);
    }

    public async Task SetTenantAsync<T>(string key, T value)
    {
        if (EmailDeliverySettingKeys.Contains(key))
            await SetEmailValueAsync(TenantId, key, SettingValueSerializer.Serialize(value));
        else
            await _tenantSettings.SetValueAsync(TenantId, key, SettingValueSerializer.Serialize(value));
        Settings.InvalidateCache(SettingScope.Tenant, TenantId);
    }

    // Commits through the policy writer without invalidating this process's settings cache.
    public async Task SetEmailValueAsync(Guid? tenantId, string key, string value)
    {
        EmailDeliverySettingsWriteResult result;
        if (key == GovernanceSettingKeys.Email.DeliveryEnabled && value == "false")
        {
            result = await MutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => new EfCoreUnitOfWork(_context).ExecuteSerializableAsync(async transactionToken =>
                {
                    var impact = (await new EmailDeliveryDisableImpactReader(_context).ReadAsync(tenantId, transactionToken))!;
                    Guid actor = ActorId;
                    return impact.CanDisable
                        ? await Writer.DisableAsync(new(TenantId: tenantId, ActorUserId: actor,
                            ExpectedRevision: impact.Revision, ConfirmationToken: _tokens.Issue(actor, impact).Token,
                            Acknowledgement: EmailDeliveryDisableConfirmation.RequiredAcknowledgement), transactionToken)
                        : await Writer.ApplyAsync([new(TenantId: tenantId, Key: key,
                            Kind: EmailDeliverySettingMutationKind.SetValue, Value: value)], actorUserId: null, transactionToken);
                }, token));
        }
        else
            result = await Writer.ApplyAsync([new(TenantId: tenantId, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetValue, Value: value)], actorUserId: null);
        if (result.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
            throw new InvalidOperationException($"SMTP fixture mutation rejected: {result.Status}.");
    }

    public async Task AddCredentialsAsync(SecretScope scope)
    {
        await AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Username, scope);
        await AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Password, scope);
    }

    public Task RejectPortUpdatesAsync()
    {
        var entity = _context.Model.FindEntityType(typeof(SystemSetting))!;
        var sql = _context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(entity.GetTableName()!);
        var key = sql.DelimitIdentifier(entity.FindProperty(nameof(SystemSetting.SettingKey))!.GetColumnName());
        var trigger = $"""
            CREATE TRIGGER reject_smtp_port_update BEFORE UPDATE ON {table}
            WHEN NEW.{key} = 'email.smtp_port'
            BEGIN SELECT RAISE(ABORT, 'SMTP port write rejected by test database'); END;
            """;
        return _context.Database.ExecuteSqlRawAsync(trigger);
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
        await _provider.DisposeAsync();
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
        File.Delete(databasePath);
    }
}
