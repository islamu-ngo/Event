using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed class NativeCustomPropertyGovernanceFactory : AuthenticatedWebApplicationFactory
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-governance-{Guid.CreateVersion7():N}.db");

    public ReportReadInterceptor Reads { get; } = new();

    public static async Task<NativeCustomPropertyGovernanceFactory> CreateAsync()
    {
        var factory = new NativeCustomPropertyGovernanceFactory();
        try
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            factory.ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(db, CancellationToken.None);
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = _path
        });
        options.UseSnakeCaseNamingConvention();
        options.AddInterceptors(Reads);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                db.TenantContext = provider.GetRequiredService<ITenantContext>();
                db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                return db;
            });
        });
    }

    internal sealed class ReportReadInterceptor : DbCommandInterceptor
    {
        public bool Fail { get; set; }
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if ((Fail || Block) && command.CommandText.Contains("custom_property_definitions", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                if (Fail)
                    throw new InvalidOperationException("governance-read-fault-sentinel");
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString());
        SqliteConnection.ClearPool(connection);
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }
}
