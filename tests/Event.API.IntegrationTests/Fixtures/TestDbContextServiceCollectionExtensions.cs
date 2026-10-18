using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

internal static class TestDbContextServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryExploreDbContext(
        this IServiceCollection services,
        string databaseName)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        return services.AddTestContextOwners();
    }

    public static IServiceCollection AddTestContextOwners(this IServiceCollection services)
    {
        services.AddSingleton<TestContextOwners>();
        foreach (Type factory in new[]
        {
            typeof(IGlobalExploreDbContextFactory), typeof(ITenantExploreDbContextFactory),
            typeof(IPdsSettlementDbContextFactory), typeof(IPdsSyncQueueAdmissionDbContextFactory),
            typeof(IEmailDispatchAdmissionDbContextFactory), typeof(IAtprotoMaterializationDbContextFactory),
            typeof(INotificationFanoutAdmissionDbContextFactory), typeof(INotificationOutboxAdmissionDbContextFactory),
            typeof(IGenericOutboxAdmissionDbContextFactory), typeof(IWebPushDispatchAdmissionDbContextFactory),
            typeof(IWebhookLocalDeliveryAdmissionDbContextFactory), typeof(IPaymentReconciliationAdmissionDbContextFactory),
            typeof(IMixedSettingsDbContextFactory)
        })
            services.Replace(ServiceDescriptor.Singleton(factory,
                sp => sp.GetRequiredService<TestContextOwners>()));
        services.AddPolicyMutationExecutionScopes();
        services.AddCheckoutDispatchPersistence();
        services.AddScoped(sp =>
        {
            if (sp.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                return selected;
            var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();

            context.ClearTenantFilterBypass();
            context.TenantContext = sp.GetService<ITenantContext>();
            context.CurrentUserService = sp.GetService<ICurrentUserService>();
            context.BindTenantLease();

            return context;
        });

        return services;
    }

    public static IServiceCollection AddPostgreSqlExploreDbContext(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        services.AddPolicyMutationExecutionScopes();
        services.AddCheckoutDispatchPersistence();
        services.AddScoped(sp =>
        {
            if (sp.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                return selected;
            var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();

            context.ClearTenantFilterBypass();
            context.TenantContext = sp.GetService<ITenantContext>();
            context.CurrentUserService = sp.GetService<ICurrentUserService>();
            context.BindTenantLease();

            return context;
        });

        return services;
    }

    private sealed class TestContextOwners(IDbContextFactory<ExploreDbContext> factory)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory, IMixedSettingsDbContextFactory,
            IPdsSettlementDbContextFactory, IPdsSyncQueueAdmissionDbContextFactory,
            IEmailDispatchAdmissionDbContextFactory, IAtprotoMaterializationDbContextFactory,
            INotificationFanoutAdmissionDbContextFactory, INotificationOutboxAdmissionDbContextFactory,
            IGenericOutboxAdmissionDbContextFactory, IWebPushDispatchAdmissionDbContextFactory,
            IWebhookLocalDeliveryAdmissionDbContextFactory, IPaymentReconciliationAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            CreateAsync(null, cancellationToken);

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            if (tenantId == Guid.Empty)
                throw new ArgumentException("An exact tenant is required.", nameof(tenantId));
            return CreateAsync(tenantId, cancellationToken);
        }

        public async Task<ExploreDbContext> CreateMixedSettingsAsync(Guid? tenantId, CancellationToken cancellationToken)
        {
            if (tenantId == Guid.Empty)
                throw new ArgumentException("An exact tenant is required.", nameof(tenantId));
            var context = await CreateAsync(tenantId, cancellationToken);
            try
            {
                if (!context.Database.IsNpgsql() && !context.Database.IsSqlite())
                    throw new NotSupportedException("mixed_settings_native_provider_not_supported");
                await context.Database.OpenConnectionAsync(cancellationToken);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        private async Task<ExploreDbContext> CreateAsync(Guid? tenantId, CancellationToken cancellationToken)
        {
            var context = await factory.CreateDbContextAsync(cancellationToken);
            context.ClearTenantFilterBypass();
            context.TenantContext = tenantId is { } tenant ? new TenantIdentity(tenant) : null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            return context;
        }

        private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    }

    public static IServiceCollection RemoveExploreDbContextRegistrations(this IServiceCollection services)
    {
        var descriptors = services
            .Where(IsExploreDbContextRegistration)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    private static bool IsExploreDbContextRegistration(ServiceDescriptor descriptor)
    {
        return IsExploreDbContextType(descriptor.ServiceType)
            || IsExploreDbContextType(descriptor.ImplementationType);
    }

    private static bool IsExploreDbContextType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(ExploreDbContext)
            || type == typeof(DbContextOptions)
            || type == typeof(DbContextOptions<ExploreDbContext>)
            || type == typeof(IDbContextFactory<ExploreDbContext>))
        {
            return true;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Contains(typeof(ExploreDbContext))
            && type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
    }
}
