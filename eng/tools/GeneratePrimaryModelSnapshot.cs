using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

bool correctiveMigration = args.Length == 4 && args[2] == "--sqlite-corrective-migration";
if (args.Length != 2 && !correctiveMigration)
{
    Console.Error.WriteLine("Usage: GeneratePrimaryModelSnapshot <PostgreSql|Sqlite|SqlServer|MySql> <EF-Design-assembly-path> [--sqlite-corrective-migration <yyyyMMddHHmmss_MigrationName>]");
    return 64;
}
if (correctiveMigration && !string.Equals(args[0], "Sqlite", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Corrective migration generation is restricted to the SQLite catalog.");

var (catalog, databaseProvider) = args[0].ToUpperInvariant() switch
{
    "POSTGRESQL" => ("Explore.Persistence", "Npgsql.EntityFrameworkCore.PostgreSQL"),
    "SQLITE" => ("Explore.Persistence.Migrations.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite"),
    "SQLSERVER" => ("Explore.Persistence.Migrations.SqlServer", "Microsoft.EntityFrameworkCore.SqlServer"),
    "MYSQL" => ("Explore.Persistence.Migrations.MySql", "Microting.EntityFrameworkCore.MySql"),
    _ => throw new ArgumentException("Select one owning catalog: PostgreSql, Sqlite, SqlServer or MySql.")
};
string root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "Explore.slnx")))
    throw new InvalidOperationException("Run from the repository root after building the selected catalog in Release.");
string catalogDirectory = Path.Combine(root, "src", catalog);
string catalogAssemblyPath = Path.Combine(catalogDirectory, "bin", "Release", "net10.0", catalog + ".dll");
string designAssemblyPath = Path.GetFullPath(args[1]);
string snapshotPath = Path.Combine(catalogDirectory, "Migrations", "ExploreDbContextModelSnapshot.cs");
foreach (string path in new[] { catalogAssemblyPath, Path.ChangeExtension(catalogAssemblyPath, ".deps.json"), designAssemblyPath })
    if (!File.Exists(path)) throw new FileNotFoundException("A built catalog and explicit EF Design assembly are required.", path);
if (!correctiveMigration && !File.Exists(snapshotPath))
    throw new FileNotFoundException("Snapshot-only generation requires an existing snapshot.", snapshotPath);

// Load existing product outputs instead of project references that could rebuild or restore them.
var resolver = new AssemblyDependencyResolver(catalogAssemblyPath);
AssemblyLoadContext.Default.Resolving += (loadContext, name) =>
{
    string? path = name.Name == "Microsoft.EntityFrameworkCore.Design"
        ? designAssemblyPath : resolver.ResolveAssemblyToPath(name);
    return path is null ? null : loadContext.LoadFromAssemblyPath(path);
};
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
{
    string? path = resolver.ResolveUnmanagedDllToPath(name);
    return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
};
Assembly catalogAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(catalogAssemblyPath);
Assembly persistence = Assembly.Load("Explore.Persistence");
Assembly ef = Assembly.Load("Microsoft.EntityFrameworkCore");
Assembly design = AssemblyLoadContext.Default.LoadFromAssemblyPath(designAssemblyPath);
string? runtimeVersion = ef.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
string? designVersion = design.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
if (runtimeVersion is null || runtimeVersion != designVersion || ef.GetName().Version?.Major != 10)
    throw new InvalidOperationException($"EF runtime ({runtimeVersion}) and Design ({designVersion}) must be matching EF 10 versions.");

Type contextType = persistence.GetType("Explore.Persistence.ExploreDbContext", throwOnError: true)!;
Type factoryType = persistence.GetType("Explore.Persistence.ExploreDbContextFactory", throwOnError: true)!;
string[] factoryArguments = ["--DesignTime:ModelOnly=true", $"--Database:Provider={args[0]}", "--Database:ServerVersion=8.4.0"];
object context = factoryType.GetMethod("CreateDbContext", [typeof(string[])])!
    .Invoke(Activator.CreateInstance(factoryType), [factoryArguments])!;
using var contextLifetime = (IDisposable)context;
Type infrastructureType = ef.GetType("Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure`1", throwOnError: true)!
    .MakeGenericType(typeof(IServiceProvider));
var contextServices = (IServiceProvider)infrastructureType.GetProperty("Instance")!.GetValue(context)!;
Type databaseProviderType = ef.GetType("Microsoft.EntityFrameworkCore.Storage.IDatabaseProvider", throwOnError: true)!;
object provider = contextServices.GetService(databaseProviderType)
    ?? throw new InvalidOperationException("The model-only context has no provider.");
if (!string.Equals((string?)databaseProviderType.GetProperty("Name")!.GetValue(provider), databaseProvider, StringComparison.Ordinal))
    throw new InvalidOperationException("The constructed model does not belong to the selected provider.");
Assembly relational = Assembly.Load("Microsoft.EntityFrameworkCore.Relational");
object database = contextType.GetProperty("Database")!.GetValue(context)!;
if (relational.GetType("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions", throwOnError: true)!
    .GetMethod("GetConnectionString")!.Invoke(null, [database]) is not null)
    throw new InvalidOperationException("Artifact generation requires a model without a connection string.");

Type modelType = ef.GetType("Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel", throwOnError: true)!;
object model = modelType.GetProperty("Model")!.GetValue(contextServices.GetService(modelType)
    ?? throw new InvalidOperationException("The full design-time model is unavailable."))!;
Assembly abstractions = Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions");
object services = Activator.CreateInstance(abstractions.GetType("Microsoft.Extensions.DependencyInjection.ServiceCollection", throwOnError: true)!)!;
Type extensions = design.GetType("Microsoft.EntityFrameworkCore.Design.DesignTimeServiceCollectionExtensions", throwOnError: true)!;
extensions.GetMethod("AddDbContextDesignTimeServices")!.Invoke(null, [services, context]);

Assembly providerAssembly = Assembly.Load(databaseProvider);
object providerAttribute = providerAssembly.GetCustomAttributes().Single(attribute =>
    attribute.GetType().FullName == "Microsoft.EntityFrameworkCore.Design.DesignTimeProviderServicesAttribute");
string providerServicesName = (string)providerAttribute.GetType().GetProperty("TypeName")!.GetValue(providerAttribute)!;
Type providerServicesType = providerAssembly.GetType(providerServicesName, throwOnError: false)
    ?? Type.GetType(providerServicesName, throwOnError: true)!;
Type designServicesType = ef.GetType("Microsoft.EntityFrameworkCore.Design.IDesignTimeServices", throwOnError: true)!;
designServicesType.GetMethod("ConfigureDesignTimeServices")!
    .Invoke(Activator.CreateInstance(providerServicesType), [services]);
extensions.GetMethod("AddEntityFrameworkDesignTimeServices")!.Invoke(null, [services, null, null]);
Assembly dependencyInjection = Assembly.Load("Microsoft.Extensions.DependencyInjection");
Type collectionType = abstractions.GetType("Microsoft.Extensions.DependencyInjection.IServiceCollection", throwOnError: true)!;
var designServices = (IServiceProvider)dependencyInjection
    .GetType("Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions", throwOnError: true)!
    .GetMethod("BuildServiceProvider", [collectionType])!.Invoke(null, [services])!;
using var designServicesLifetime = (IDisposable)designServices;
Type selectorType = design.GetType("Microsoft.EntityFrameworkCore.Migrations.Design.IMigrationsCodeGeneratorSelector", throwOnError: true)!;
object selector = designServices.GetService(selectorType)
    ?? throw new InvalidOperationException("Native EF migration generation services are unavailable.");
object generator = selectorType.GetMethod("Select")!.Invoke(selector, ["C#"])!;
Type generatorType = design.GetType("Microsoft.EntityFrameworkCore.Migrations.Design.IMigrationsCodeGenerator", throwOnError: true)!;
if (correctiveMigration)
{
    GenerateSqliteCorrectiveMigration(relational, model, contextServices, generatorType, generator,
        contextType, Path.GetDirectoryName(snapshotPath)!, args[3]);
    return 0;
}
string snapshotNamespace = catalogAssembly.GetName().Name + ".Migrations";
string generated = (string)generatorType.GetMethod("GenerateSnapshot")!
    .Invoke(generator, [snapshotNamespace, contextType, "ExploreDbContextModelSnapshot", model])!;
if (string.IsNullOrWhiteSpace(generated))
    throw new InvalidOperationException("Native EF generation returned an empty snapshot.");

Console.Error.WriteLine("Snapshot-only generation does not prove combined migration-history coverage or chronological DDL safety.");
string temporaryPath = snapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    File.WriteAllText(temporaryPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    File.Move(temporaryPath, snapshotPath, overwrite: true);
}
finally
{
    File.Delete(temporaryPath);
}
Console.WriteLine($"Generated {snapshotPath} using EF {runtimeVersion}; migration/designer histories unchanged.");
return 0;

static void GenerateSqliteCorrectiveMigration(
    Assembly relational, object model, IServiceProvider contextServices, Type generatorType, object generator,
    Type contextType, string migrationDirectory, string migrationId)
{
    if (!Regex.IsMatch(migrationId, @"\A[0-9]{14}_[A-Za-z_][A-Za-z0-9_]*\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        throw new ArgumentException("Supply an explicit timestamped migration ID with a simple C# identifier.");
    Type idType = relational.GetType("Microsoft.EntityFrameworkCore.Migrations.IMigrationsIdGenerator", throwOnError: true)!;
    object ids = contextServices.GetService(idType)
        ?? throw new InvalidOperationException("Native EF migration ID services are unavailable.");
    if (!(bool)idType.GetMethod("IsValidId")!.Invoke(ids, [migrationId])!)
        throw new ArgumentException("The supplied migration ID is not valid for the owning EF catalog.");
    string migrationName = (string)idType.GetMethod("GetName")!.Invoke(ids, [migrationId])!;
    Type assemblyType = relational.GetType("Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly", throwOnError: true)!;
    object assembly = contextServices.GetService(assemblyType)
        ?? throw new InvalidOperationException("The owning migration assembly is unavailable.");
    var migrations = (IReadOnlyDictionary<string, TypeInfo>)assemblyType.GetProperty("Migrations")!.GetValue(assembly)!;
    string latestId = migrations.Keys.Order(StringComparer.Ordinal).Last();
    string migrationNamespace = migrations[latestId].Namespace
        ?? throw new InvalidOperationException("The owning migration namespace is missing.");
    string[] sourceIds = Directory.EnumerateFiles(migrationDirectory, "*.cs")
        .Select(path => Path.GetFileNameWithoutExtension(path)!)
        .Where(id => (bool)idType.GetMethod("IsValidId")!.Invoke(ids, [id])!).ToArray();
    if (migrations.Keys.Concat(sourceIds).Any(id => string.CompareOrdinal(id, migrationId) >= 0)
        || migrations.Values.Any(type => type.Name == migrationName)
        || sourceIds.Any(id => (string)idType.GetMethod("GetName")!.Invoke(ids, [id])! == migrationName))
        throw new InvalidOperationException("The corrective migration must have a new name and sort after all existing SQLite history.");
    string migrationPath = Path.Combine(migrationDirectory, migrationId + ".cs");
    string metadataPath = Path.Combine(migrationDirectory, migrationId + ".Designer.cs");
    if (File.Exists(migrationPath) || File.Exists(metadataPath))
        throw new IOException("Corrective generation never overwrites existing migration or designer output.");

    Type relationalModelType = relational.GetType("Microsoft.EntityFrameworkCore.Metadata.IRelationalModel", throwOnError: true)!;
    object relationalModel = relational.GetType("Microsoft.EntityFrameworkCore.RelationalModelExtensions", throwOnError: true)!
        .GetMethod("GetRelationalModel")!.Invoke(null, [model])!;
    Type tableType = relational.GetType("Microsoft.EntityFrameworkCore.Metadata.ITable", throwOnError: true)!;
    Type checkType = relational.GetType("Microsoft.EntityFrameworkCore.Metadata.IReadOnlyCheckConstraint", throwOnError: true)!;
    const string checkName = "ck_event_resources_relative_availability_order";
    object[] Checks(object candidateTable) => ((IEnumerable)tableType.GetProperty("CheckConstraints")!.GetValue(candidateTable)!).Cast<object>().ToArray();
    string CheckName(object check) => (string)checkType.GetProperty("Name")!.GetValue(check)!;
    object table = ((IEnumerable)relationalModelType.GetProperty("Tables")!.GetValue(relationalModel)!).Cast<object>()
        .Single(candidate => Checks(candidate).Any(check => CheckName(check) == checkName));
    object[] checks = Checks(table);
    if (new[] { "material_version_id", "moderation_quarantine_count" }
            .Any(name => tableType.GetMethod("FindColumn", [typeof(string)])!.Invoke(table, [name]) is null)
        || new[] { "ck_event_resources_material", "ck_event_resources_quarantine" }
            .Any(name => !checks.Any(check => CheckName(check) == name)))
        throw new InvalidOperationException("The full merged resource model must retain the native moderation columns and checks.");

    Type addType = relational.GetType("Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation", throwOnError: true)!;
    object up = addType.GetMethod("CreateFrom")!.Invoke(null, [checks.Single(check => CheckName(check) == checkName)])!;
    string tableName = (string)addType.GetProperty("Table")!.GetValue(up)!;
    object? schema = addType.GetProperty("Schema")!.GetValue(up);
    if (schema is not null || !tableName.EndsWith("event_resources", StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace((string?)addType.GetProperty("Sql")!.GetValue(up)))
        throw new InvalidOperationException("The model check must belong to the prefixed SQLite resource table and have native SQL.");
    Type dropType = relational.GetType("Microsoft.EntityFrameworkCore.Migrations.Operations.DropCheckConstraintOperation", throwOnError: true)!;
    object down = Activator.CreateInstance(dropType)!;
    dropType.GetProperty("Name")!.SetValue(down, checkName);
    dropType.GetProperty("Table")!.SetValue(down, tableName);
    dropType.GetProperty("Schema")!.SetValue(down, schema);
    Type operationType = relational.GetType("Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation", throwOnError: true)!;
    Array upOperations = Array.CreateInstance(operationType, 1);
    upOperations.SetValue(up, 0);
    Array downOperations = Array.CreateInstance(operationType, 1);
    downOperations.SetValue(down, 0);
    string migration = (string)generatorType.GetMethod("GenerateMigration")!
        .Invoke(generator, [migrationNamespace, migrationName, upOperations, downOperations])!;
    string metadata = (string)generatorType.GetMethod("GenerateMetadata")!
        .Invoke(generator, [migrationNamespace, contextType, migrationName, migrationId, model])!;
    if (string.IsNullOrWhiteSpace(migration) || string.IsNullOrWhiteSpace(metadata))
        throw new InvalidOperationException("Native EF generation returned an empty migration or designer.");

    bool migrationCreated = false, metadataCreated = false;
    try
    {
        using var migrationFile = new FileStream(migrationPath, FileMode.CreateNew, FileAccess.Write);
        migrationCreated = true;
        using var metadataFile = new FileStream(metadataPath, FileMode.CreateNew, FileAccess.Write);
        metadataCreated = true;
        using var migrationWriter = new StreamWriter(migrationFile, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var metadataWriter = new StreamWriter(metadataFile, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        migrationWriter.Write(migration);
        metadataWriter.Write(metadata);
    }
    catch
    {
        if (metadataCreated) File.Delete(metadataPath);
        if (migrationCreated) File.Delete(migrationPath);
        throw;
    }
    Console.WriteLine($"Generated {migrationPath} and {metadataPath} from the full merged EF model; existing snapshots and histories unchanged.");
    Console.Error.WriteLine("This tail correction repairs the resource check; generation alone does not verify migration execution, existing-row validity or SQLite history safety.");
}
