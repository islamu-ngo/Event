using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

internal sealed class RelationalNamespaceOptionsExtension(string modelSchema, string targetSchema) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public string ModelSchema { get; } = modelSchema;

    public string TargetSchema { get; } = targetSchema;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(RelationalNamespaceOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        private new RelationalNamespaceOptionsExtension Extension =>
            (RelationalNamespaceOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            $"PrimaryModelSchema={Extension.ModelSchema} PrimaryTargetSchema={Extension.TargetSchema} ";

        public override int GetServiceProviderHashCode() =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(Extension.ModelSchema),
                StringComparer.Ordinal.GetHashCode(Extension.TargetSchema));

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Explore:PrimaryModelSchema"] = Extension.ModelSchema;
            debugInfo["Explore:PrimaryTargetSchema"] = Extension.TargetSchema;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo &&
            StringComparer.Ordinal.Equals(Extension.ModelSchema, otherInfo.Extension.ModelSchema) &&
            StringComparer.Ordinal.Equals(Extension.TargetSchema, otherInfo.Extension.TargetSchema);
    }
}
