using System.Reflection;
using ISLAMU.Event.SetupAssistant.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal sealed record SetupCliOptionDescriptor(string Name, bool RequiresValue, bool Repeatable);

internal sealed record SetupCliOperationDescriptor(
    string Name,
    Type SettingsType,
    IReadOnlyList<SetupCliOptionDescriptor> Options,
    Action<IConfigurator<SetupCliCommandSettings>, string, SetupCliCommandRuntime> RegisterBranch,
    Action<IConfigurator, string, SetupCliCommandRuntime> RegisterRoot);

internal sealed record SetupCliFamilyDescriptor(string Name, IReadOnlyList<SetupCliOperationDescriptor> Operations);

internal static class SetupCliCommandRegistry
{
    internal static IReadOnlyList<SetupCliFamilyDescriptor> Families { get; } =
    [
        CatalogueCommands.Descriptor,
        ManifestCommands.Descriptor,
        TenantPackageCommands.Descriptor,
        PortabilityCommands.Descriptor,
        EnvironmentCommands.Descriptor,
        LegalCommands.Descriptor,
        DoctorCommands.Descriptor
    ];

    internal static CommandApp Create(SetupCliCommandRuntime runtime, IAnsiConsole console)
    {
        var app = new CommandApp(new SetupCliTypeRegistrar(console));
        app.Configure(configuration =>
        {
            configuration.SetApplicationName(SetupCliExecutableMarker.ExecutableName);
            foreach (SetupCliFamilyDescriptor family in Families)
            {
                if (family.Name == "doctor")
                {
                    family.Operations[0].RegisterRoot(configuration, family.Name, runtime);
                    continue;
                }

                configuration.AddBranch<SetupCliCommandSettings>(family.Name, branch =>
                {
                    foreach (SetupCliOperationDescriptor operation in family.Operations)
                        operation.RegisterBranch(branch, family.Name, runtime);
                });
            }
            configuration.AddExample(["doctor", "--machine"]);
        });
        return app;
    }

    internal static SetupCliFamilyDescriptor Family(string name, params SetupCliOperationDescriptor[] operations) =>
        new(name, Array.AsReadOnly(operations));

    internal static SetupCliOperationDescriptor Operation<TSettings>(string name)
        where TSettings : SetupCliCommandSettings =>
        new(
            name,
            typeof(TSettings),
            DescribeOptions(typeof(TSettings)),
            (branch, family, runtime) => branch.AddDelegate<TSettings>(
                name,
                (_, settings, _) => runtime.Execute(family, name, settings)),
            (root, family, runtime) => root.AddDelegate<TSettings>(
                name,
                (_, settings, _) => runtime.Execute(family, name, settings)));

    internal static bool TryResolve(string family, string operation, out SetupCliOperationDescriptor? descriptor)
    {
        descriptor = Families.FirstOrDefault(item => item.Name == family)?.Operations
            .FirstOrDefault(item => item.Name == operation);
        return descriptor is not null;
    }

    private static IReadOnlyList<SetupCliOptionDescriptor> DescribeOptions(Type settingsType) =>
        settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<CommandOptionAttribute>()))
            .Where(item => item.Attribute is not null)
            .SelectMany(item => item.Attribute!.LongNames.Select(name => new SetupCliOptionDescriptor(
                "--" + name,
                item.Attribute.ValueName is not null,
                item.Property.PropertyType == typeof(string[]))))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

    private sealed class SetupCliTypeRegistrar(IAnsiConsole console) : ITypeRegistrar
    {
        private readonly Dictionary<Type, Func<object>> _registrations = new()
        {
            [typeof(IAnsiConsole)] = () => console
        };

        public ITypeResolver Build() => new Resolver(_registrations);
        public void Register(Type service, Type implementation) =>
            _registrations[service] = () => Activator.CreateInstance(implementation)
                ?? throw new InvalidOperationException("cli-service-unavailable");
        public void RegisterInstance(Type service, object implementation) =>
            _registrations[service] = () => implementation;
        public void RegisterLazy(Type service, Func<object> factory) =>
            _registrations[service] = factory;

        private sealed class Resolver(IReadOnlyDictionary<Type, Func<object>> registrations) : ITypeResolver, IDisposable
        {
            public object? Resolve(Type? type)
            {
                if (type is null)
                    return null;
                if (registrations.TryGetValue(type, out Func<object>? factory))
                    return factory();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return Array.CreateInstance(type.GetGenericArguments()[0], 0);
                return Activator.CreateInstance(type);
            }

            public void Dispose()
            {
            }
        }
    }
}
