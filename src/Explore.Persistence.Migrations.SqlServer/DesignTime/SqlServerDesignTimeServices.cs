using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: DesignTimeServicesReference(
    "Explore.Persistence.Migrations.SqlServer.DesignTime.SqlServerDesignTimeServices, Explore.Persistence.Migrations.SqlServer",
    "Microsoft.EntityFrameworkCore.SqlServer")]

namespace Explore.Persistence.Migrations.SqlServer.DesignTime;

/// <summary>
/// Preserves SQL Server ownership delete policies in generated migration models.
/// </summary>
public sealed class SqlServerDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection) =>
        serviceCollection.Replace(
            ServiceDescriptor.Singleton<ICSharpSnapshotGenerator, SqlServerSnapshotGenerator>());
}
