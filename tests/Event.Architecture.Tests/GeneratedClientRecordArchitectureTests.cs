using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    public async Task EmbeddedClientBytesMatchTheReferencedCompilersSourceChecksum()
    {
        using FileStream symbols = File.OpenRead(Path.ChangeExtension(typeof(ActorDto).Assembly.Location, ".pdb"));
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(symbols);
        MetadataReader metadata = provider.GetMetadataReader();
        Document document = metadata.Documents.Select(metadata.GetDocument).Single(candidate =>
            metadata.GetString(candidate.Name).Replace('\\', '/')
                .EndsWith("/generated-contracts/EventApiTagClients.g.cs", StringComparison.Ordinal));
        await Assert.That(metadata.GetGuid(document.HashAlgorithm))
            .IsEqualTo(new Guid("8829d00f-11b8-4213-878b-770e8597ac16"));
        using Stream source = typeof(GeneratedContractInputs).Assembly
            .GetManifestResourceStream("GeneratedContracts.EventApiTagClients.g.cs")
            ?? throw new InvalidOperationException("Missing compiled client capture.");
        await Assert.That(Convert.ToHexString(SHA256.HashData(source)))
            .IsEqualTo(Convert.ToHexString(metadata.GetBlobBytes(document.Hash)))
            .Because("the embedded capture must be the bytes consumed by the referenced client's compiler");
    }

    [Test]
    public async Task GeneratedNominalRecordSurfaceIsExactAndInitOnly()
    {
        string source = GeneratedContractInputs.Client;
        HashSet<string> mutableTypes =
            GeneratedContractPolicy.ParseMutableStateTypes(
                GeneratedContractInputs.MutablePolicy.Split('\n'));
        GeneratedContractClassification classification =
            GeneratedContractTransformer.Classify(
                source,
                mutableTypes);
        string[] names = source.Split('\n')
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
        string[] names = GeneratedContractInputs.MutablePolicy.Split('\n')
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
}
