// ABOUTME: Exercises SMTP capability and transport ownership through real relational settings resolution.
// ABOUTME: Guards delivery disablement, governance locks, and cross-scope credential disclosure.

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
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
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class SmtpConfigResolverTests
{
    [Test]
    public async Task ResolveAsync_DeliveryDisabled_DoesNotReadSecrets()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_NoDeliverySetting_DefaultsToDisabled()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_EnabledAnonymousInstance_IsAvailable()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport).IsNotNull();
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
        await Assert.That(transport.Security).IsEqualTo(SmtpSecurityMode.StartTls);
        await Assert.That(transport.Username).IsNull();
        await Assert.That(transport.Password).IsNull();
    }

    [Test]
    public async Task ResolveAsync_InstanceTransport_ResolvesOnlyInstanceCredentials()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        var credentials = fixture.ResolveCredentials(SecretScope.Instance, null);

        var transport = await fixture.Smtp.ResolveAsync();
        var json = JsonSerializer.Serialize(await fixture.Capabilities.ResolveAsync(fixture.TenantId));

        await Assert.That(transport!.Username).IsEqualTo(credentials.Username);
        await Assert.That(transport.Password).IsEqualTo(credentials.Password);
        await Assert.That(json).DoesNotContain(credentials.Username);
        await Assert.That(json).DoesNotContain(credentials.Password);
        await Assert.That(json).DoesNotContain(transport.Host);
        await Assert.That(json).DoesNotContain(transport.FromAddress);
    }

    [Test]
    public async Task ResolveAsync_TenantHostWithoutOwnBindings_DoesNotInheritInstanceCredentials()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        await fixture.ConfigureTenantAsync();
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@tenant.test");
        await Assert.That(transport.Username).IsNull();
        await Assert.That(transport.Password).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantEnablesOwnTransport_DoesNotEnableInstanceOrOtherTenants()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.ConfigureTenantAsync();
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        fixture.RejectSecretReads();

        var tenant = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var instance = await fixture.Capabilities.ResolveAsync(null);
        var otherTenant = await fixture.Capabilities.ResolveAsync(Guid.CreateVersion7());
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(tenant.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(tenant.Enabled).IsTrue();
        await Assert.That(tenant.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(tenant.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(instance.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(instance.Enabled).IsFalse();
        await Assert.That(otherTenant.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(otherTenant.Enabled).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_TenantEnableWithoutOwnHost_CannotActivateDisabledInstanceTransport()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantCredentials_ComposesTenantTransport()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        var credentials = fixture.ResolveCredentials(SecretScope.Tenant, fixture.TenantId);

        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(transport.Username).IsEqualTo(credentials.Username);
        await Assert.That(transport.Password).IsEqualTo(credentials.Password);
    }

    [Test]
    [Arguments(SecretScope.Instance)]
    [Arguments(SecretScope.Tenant)]
    public async Task ResolveAsync_TenantCredentialsReturnedFromWrongOwner_FailsClosed(SecretScope returnedScope)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        fixture.ResolveCredentials(returnedScope, returnedScope == SecretScope.Instance ? null : Guid.CreateVersion7(),
            requestedTenantId: fixture.TenantId);

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(capability.ReasonCode).IsNotNullOrEmpty();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantHostWithoutOwnSender_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.tenant.test");

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResolveAsync_GovernanceLock_UsesInstanceTransport(bool explicitLock)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.tenant.test");
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.FromAddress, "events@tenant.test");
        if (explicitLock)
            await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, true);

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
    }

    [Test]
    public async Task ResolveAsync_InstanceHostLock_OverridesTenantDelegation()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.Settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
            Guid.Empty, Guid.Empty);

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
    }

    [Test]
    public async Task ResolveAsync_DisabledAfterResolution_DoesNotReuseTransportOrReadSecrets()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.ResolveCredentials(SecretScope.Instance, null);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNotNull();

        await fixture.Settings.SetValueAsync(GovernanceSettingKeys.Email.DeliveryEnabled, "false", SettingScope.Instance,
            Guid.Empty, Guid.Empty);
        fixture.RejectSecretReads();

        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
        await Assert.That((await fixture.Capabilities.ResolveAsync(null)).State)
            .IsEqualTo(EmailDeliveryState.Disabled);
    }

    [Test]
    public async Task ResolveAsync_EnabledWithoutHost_IsUnconfigured()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Unconfigured);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_EnabledWithoutSender_IsMisconfigured()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.instance.test");

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_PartialCredentials_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        fixture.Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Username, null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Smtp.Username, Guid.CreateVersion7().ToString("N"),
                SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_UnauthorizedCredentials_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.Secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unauthorized);

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Degraded);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_ResolvedEmptyCredentials_DoesNotDowngradeToAnonymous()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        fixture.Secrets.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(call => SecretResolutionResult.Resolved(new ResolvedSecret(call.ArgAt<string>(0),
                string.Empty, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));

        await Assert.That((await fixture.Capabilities.ResolveAsync(null)).State)
            .IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    [Arguments(SecretScope.Instance, true)]
    [Arguments(SecretScope.Instance, false)]
    [Arguments(SecretScope.Tenant, true)]
    [Arguments(SecretScope.Tenant, false)]
    public async Task ResolveAsync_DeclaredCredentialsMissingFromAuthority_DoesNotDowngradeToAnonymous(
        SecretScope scope, bool completeBindings)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        if (scope == SecretScope.Tenant)
            await fixture.ConfigureTenantAsync();
        await fixture.AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Username, scope);
        if (completeBindings)
            await fixture.AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Password, scope);

        await Assert.That((await fixture.Capabilities.ResolveAsync(fixture.TenantId)).State)
            .IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ApplySettingsAsync_LockedHost_PreservesLockAndInstanceOwnership()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.Settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
            Guid.Empty, Guid.Empty);

        await fixture.InstanceSmtp.ApplySettingsAsync(new InstanceSmtpSettingsDto
        {
            Host = "smtp.updated-instance.test", FromAddress = "events@updated-instance.test"
        });
        fixture.Settings.InvalidateCache(SettingScope.Instance);

        var setting = await fixture.Settings.ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost,
            new SettingContext(fixture.TenantId));
        var transport = await fixture.Smtp.ResolveAsync();
        await Assert.That(setting!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(transport!.Host).IsEqualTo("smtp.updated-instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@updated-instance.test");
    }

    [Test]
    public async Task ApplySettingsAsync_LaterWriteFails_RollsBackEarlierHostWrite()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.RejectPortUpdatesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.InstanceSmtp.ApplySettingsAsync(
            new InstanceSmtpSettingsDto { Host = "smtp.rollback.test", Port = 2525, FromAddress = "events@rollback.test" }));

        var settings = await fixture.InstanceSmtp.ReadSettingsAsync();
        await Assert.That(settings.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(settings.Port).IsEqualTo(587);
        await Assert.That(settings.FromAddress).IsEqualTo("events@instance.test");
    }

    private sealed class SettingsDatabase : IAsyncDisposable, ITenantContext
    {
        private readonly SqliteConnection _connection;
        private readonly ExploreDbContext _context;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly SystemSettingRepository _systemSettings;
        private readonly TenantSettingRepository _tenantSettings;
        private readonly SecretBindingRepository _bindings;
        private readonly Dictionary<Guid, string> _bindingKeys = [];

        private SettingsDatabase(SqliteConnection connection, ExploreDbContext context)
        {
            _connection = connection;
            _context = context;
            var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
            _systemSettings = new SystemSettingRepository(context, mutationLock);
            _tenantSettings = new TenantSettingRepository(context);
            _bindings = new SecretBindingRepository(context);
            Settings = new HierarchicalSettingsResolver(_systemSettings, _tenantSettings,
                new OrganizationSettingRepository(context), new GroupSettingRepository(context),
                new GroupTenantRepository(context), new UserPreferenceRepository(context), this,
                mutationLock, _cache, NullLogger<HierarchicalSettingsResolver>.Instance);
            Capabilities = new EmailDeliveryCapabilityResolver(Settings, Secrets, _bindings);
            Smtp = new SmtpConfigResolver(Capabilities, this, Settings);
            InstanceSmtp = new InstanceSmtpSettingService(_systemSettings, mutationLock);
            Secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Unconfigured);
            Secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Unconfigured);
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public ISecretResolver Secrets { get; } = Substitute.For<ISecretResolver>();
        public IHierarchicalSettingsResolver Settings { get; }
        public EmailDeliveryCapabilityResolver Capabilities { get; }
        public ISmtpConfigResolver Smtp { get; }
        public InstanceSmtpSettingService InstanceSmtp { get; }

        public static async Task<SettingsDatabase> CreateAsync()
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
            var fixture = new SettingsDatabase(connection, context);
            context.AddRange(
                new SettingValueTypeLookup { Id = 0, MasterCode = "String", FullName = "String" },
                new SettingValueTypeLookup { Id = 1, MasterCode = "Integer", FullName = "Integer" },
                new SettingValueTypeLookup { Id = 2, MasterCode = "Boolean", FullName = "Boolean" },
                new SettingScopeLookup { Id = 1, MasterCode = "Instance", FullName = "Instance" },
                new SettingScopeLookup { Id = 2, MasterCode = "Tenant", FullName = "Tenant" },
                new SecretSourceTypeLookup { Id = (int)SecretSourceType.EnvironmentVariable, MasterCode = "EnvironmentVariable", FullName = "Environment variable" },
                new SecretValidationStatus { Id = (int)SecretValidationResult.NotValidated, MasterCode = "NotValidated", FullName = "Not validated" },
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
            await _systemSettings.UpsertAsync(new SystemSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = key, Value = SettingValueSerializer.Serialize(value),
                ValueType = SettingValueType.String, CreatedAt = DateTime.UtcNow
            });
            Settings.InvalidateCache(SettingScope.Instance);
        }

        public async Task SetTenantAsync<T>(string key, T value)
        {
            await _tenantSettings.SetValueAsync(TenantId, key, SettingValueSerializer.Serialize(value));
            Settings.InvalidateCache(SettingScope.Tenant, TenantId);
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
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
            File.Delete(databasePath);
        }
    }
}
