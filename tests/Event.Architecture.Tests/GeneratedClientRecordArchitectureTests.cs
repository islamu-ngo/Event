using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Explore.GeneratedContracts;
using Explore.Blazor.Client.Clients;

namespace Event.Architecture.Tests;

public sealed class GeneratedClientRecordArchitectureTests
{
    private const string RecordDeclaration = "public partial record class ";
    private const string PolicyStamp =
        "// <generated-record-policy version=\"1\">";

    [Test]
    public async Task GeneratedNominalRecordSurfaceIsExactAndInitOnly()
    {
        string source = File.ReadAllText(GeneratedClientPath());
        HashSet<string> mutableTypes =
            GeneratedContractPolicy.LoadMutableStateTypes(
                MutablePolicyPath());
        GeneratedContractClassification classification =
            GeneratedContractTransformer.Classify(
                source,
                mutableTypes);
        string[] names = File.ReadLines(GeneratedClientPath())
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(
                RecordDeclaration,
                StringComparison.Ordinal))
            .Select(line => line[RecordDeclaration.Length..]
                .Split(
                    [' ', '<'],
                    StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Type[] records = names
            .Select(ResolveGeneratedType)
            .ToArray();
        string[] nonRecords = records
            .Where(type => !IsRecord(type))
            .Select(type => type.FullName!)
            .ToArray();
        string[] mutableProperties = records
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod?.IsPublic == true)
            .Where(property => property.GetCustomAttribute<
                JsonExtensionDataAttribute>() is null)
            .Where(property => !property.SetMethod!.ReturnParameter
                .GetRequiredCustomModifiers()
                .Contains(typeof(IsExternalInit)))
            .Select(property =>
                $"{property.DeclaringType!.FullName}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(source).Contains(PolicyStamp);
        await Assert.That(names)
            .IsEquivalentTo(classification.RecordTypeNames);
        await Assert.That(records.Length)
            .IsEqualTo(classification.RecordTypeNames.Count);
        await Assert.That(nonRecords).IsEmpty();
        await Assert.That(mutableProperties).IsEmpty();
    }

    [Test]
    public async Task GeneratedRecordsOmitSensitiveValuesFromDiagnosticText()
    {
        const string sentinel =
            "phase11-sensitive-value-must-never-print";
        object[] sensitiveContracts =
        [
            new WebhookProviderPortalAccessDto(),
            new UserDto(),
            new SharedContactDto(),
        ];
        foreach (object contract in sensitiveContracts)
        {
            foreach (PropertyInfo property in contract.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string)
                    && property.SetMethod?.IsPublic == true))
            {
                property.SetValue(contract, sentinel);
            }
        }

        string[] leaks = sensitiveContracts
            .Select(contract => contract.ToString() ?? string.Empty)
            .Where(text => text.Contains(
                sentinel,
                StringComparison.Ordinal))
            .ToArray();

        await Assert.That(leaks).IsEmpty();
    }

    [Test]
    public async Task MutableGeneratedContractManifestIsExactAndClassBased()
    {
        string[] names = File.ReadAllLines(MutablePolicyPath())
            .Select(line => line.Trim())
            .Where(line => line.Length != 0
                && !line.StartsWith('#'))
            .ToArray();
        Type[] types = names.Select(ResolveGeneratedType).ToArray();
        string[] accidentalRecords = types
            .Where(IsRecord)
            .Select(type => type.FullName!)
            .ToArray();
        string[] immutableClasses = types
            .Where(type => !type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.SetMethod?.IsPublic == true
                    && !property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit))))
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(names.Length).IsEqualTo(25);
        await Assert.That(names.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(names.Length);
        await Assert.That(accidentalRecords).IsEmpty();
        await Assert.That(immutableClasses).IsEmpty();
    }

    [Test]
    public async Task FrameworkSensitiveGeneratedTypesRemainClasses()
    {
        Type[] protectedTypes =
        [
            typeof(PublicExperienceClient),
            typeof(ApiException),
            typeof(ApiException<>),
            typeof(FileResponse),
            typeof(FileContentResult),
            typeof(HalResourceOfActorDto),
            typeof(HalCollectionResourceOfActorListDto),
            typeof(PatchTenantFooterSettingsDto),
            typeof(UpdateCategoryDto),
        ];

        await Assert.That(protectedTypes.Where(IsRecord)).IsEmpty();
    }

    private static Type ResolveGeneratedType(string name) =>
        typeof(ActorDto).Assembly.GetType(
            $"{typeof(ActorDto).Namespace}.{name}",
            throwOnError: true)!;

    private static bool IsRecord(Type type) =>
        type.GetMethod(
            "<Clone>$",
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance)
        is not null;

    private static string GeneratedClientPath() => Path.Combine(
        RepositoryRoot(),
        "src",
        "Explore.Blazor.Client",
        "Clients",
        "EventApiTagClients.g.cs");

    private static string MutablePolicyPath() => Path.Combine(
        RepositoryRoot(),
        "eng",
        "tools",
        "Explore.GeneratedContracts",
        "mutable-generated-contracts.txt");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Repository root containing Explore.slnx was not found.");
    }
}
