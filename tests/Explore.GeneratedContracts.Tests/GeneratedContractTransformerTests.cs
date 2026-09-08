using Explore.GeneratedContracts;

namespace Explore.GeneratedContracts.Tests;

public sealed class GeneratedContractTransformerTests
{
    [Test]
    public async Task TransformConvertsOnlyExactEligibleTypes()
    {
        GeneratedContractTransformer.TransformOutput output =
            GeneratedContractTransformer.Transform(
                GeneratedSource,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "MutableViewModel",
                });

        await Assert.That(output.RecordCount).IsEqualTo(2);
        await Assert.That(output.Text)
            .Contains("public partial record class ResponseDto");
        await Assert.That(output.Text)
            .Contains("public partial record class NestedResponseDto");
        await Assert.That(output.Text)
            .Contains("public partial class RequestDto");
        await Assert.That(output.Text)
            .Contains("public partial class NestedInputDto");
        await Assert.That(output.Text)
            .Contains("public partial class HalEnvelopeDto");
        await Assert.That(output.Text)
            .Contains("public partial class DerivedDto");
        await Assert.That(output.Text)
            .Contains("public partial class MutableViewModel");
    }

    [Test]
    public async Task TransformUsesInitButPreservesExtensionDataSetter()
    {
        GeneratedContractTransformer.TransformOutput output =
            GeneratedContractTransformer.Transform(
                GeneratedSource,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "MutableViewModel",
                });
        string response = ExtractType(output.Text, "ResponseDto");

        await Assert.That(response)
            .Contains("public string? Value { get; init; }");
        await Assert.That(response)
            .Contains("AdditionalProperties { get; set; }");
        await Assert.That(response)
            .Contains("Generated record values are intentionally omitted");
        await Assert.That(response)
            .Contains("protected virtual bool PrintMembers");
        await Assert.That(response)
            .Contains("return false;");
    }

    [Test]
    public async Task TransformIsByteStableAndPolicyReversible()
    {
        HashSet<string> mutableTypes = new(StringComparer.Ordinal)
        {
            "MutableViewModel",
        };
        GeneratedContractTransformer.TransformOutput first =
            GeneratedContractTransformer.Transform(
                GeneratedSource,
                mutableTypes);
        GeneratedContractTransformer.TransformOutput second =
            GeneratedContractTransformer.Transform(
                first.Text,
                mutableTypes);
        HashSet<string> expandedMutableTypes =
            new(mutableTypes, StringComparer.Ordinal)
            {
                "ResponseDto",
            };
        GeneratedContractTransformer.TransformOutput reverted =
            GeneratedContractTransformer.Transform(
                first.Text,
                expandedMutableTypes);
        string revertedResponse =
            ExtractType(reverted.Text, "ResponseDto");

        await Assert.That(second.Text).IsEqualTo(first.Text);
        await Assert.That(ExtractType(first.Text, "ResponseDto"))
            .DoesNotContain("\n\n\n");
        await Assert.That(revertedResponse)
            .Contains("public partial class ResponseDto");
        await Assert.That(revertedResponse)
            .Contains("public string? Value { get; set; }");
        await Assert.That(revertedResponse)
            .DoesNotContain("Generated record values are intentionally omitted");
    }

    [Test]
    public async Task ClassificationReturnsExactProtocolClosureAndRecords()
    {
        GeneratedContractClassification classification =
            GeneratedContractTransformer.Classify(
                GeneratedSource,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "MutableViewModel",
                });

        await Assert.That(classification.ProtocolInputTypeNames)
            .IsEquivalentTo(["NestedInputDto", "RequestDto"]);
        await Assert.That(classification.RecordTypeNames)
            .IsEquivalentTo(["NestedResponseDto", "ResponseDto"]);
    }

    [Test]
    public async Task TransformRejectsMalformedSourceAndUnknownMutableTypes()
    {
        await Assert.That(() =>
                GeneratedContractTransformer.Transform(
                    "public class Broken {",
                    new HashSet<string>(StringComparer.Ordinal)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                GeneratedContractTransformer.Transform(
                    GeneratedSource,
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        "UnknownType",
                    }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MutableManifestRejectsMissingAndDuplicateEntries()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"generated-contract-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string missing = Path.Combine(directory, "missing.txt");
        string duplicate = Path.Combine(directory, "duplicate.txt");
        await File.WriteAllTextAsync(
            duplicate,
            "MutableViewModel\nMutableViewModel\n");

        try
        {
            await Assert.That(() =>
                    GeneratedContractPolicy.LoadMutableStateTypes(missing))
                .Throws<FileNotFoundException>();
            await Assert.That(() =>
                    GeneratedContractPolicy.LoadMutableStateTypes(duplicate))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ExtractType(string source, string typeName)
    {
        int start = FindTypeStart(source, typeName);
        if (start < 0)
        {
            return string.Empty;
        }

        int end = source.IndexOf(
            "\n[System.CodeDom.Compiler.GeneratedCode",
            start + 1,
            StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static int FindTypeStart(string source, string typeName)
    {
        int recordStart = source.IndexOf(
            $"public partial record class {typeName}",
            StringComparison.Ordinal);
        return recordStart >= 0
            ? recordStart
            : source.IndexOf(
                $"public partial class {typeName}",
                StringComparison.Ordinal);
    }

    [Test]
    public async Task ClassificationUnionsProtocolInputsAcrossEveryGeneratedClient()
    {
        GeneratedContractClassification classification =
            GeneratedContractTransformer.Classify(
                MultipleClientGeneratedSource,
                new HashSet<string>(StringComparer.Ordinal));

        await Assert.That(classification.ProtocolInputTypeNames)
            .IsEquivalentTo(
            [
                "EventRequestDto",
                "NestedInputDto",
                "RegistrationRequestDto",
            ]);
        await Assert.That(classification.RecordTypeNames)
            .IsEquivalentTo(["SharedResponseDto"]);
    }

    [Test]
    public async Task ClassificationRejectsSourceWithoutAnyGeneratedClient()
    {
        await Assert.That(() =>
                GeneratedContractTransformer.Classify(
                    ClientlessGeneratedSource,
                    new HashSet<string>(StringComparer.Ordinal)))
            .Throws<InvalidOperationException>();
    }

    private const string MultipleClientGeneratedSource =
        """
        #nullable enable

        namespace Generated
        {
        [System.CodeDom.Compiler.GeneratedCode("NSwag", "test")]
        public partial interface IEventClient
        {
            void CreateEvent(EventRequestDto body);

            SharedResponseDto GetEvent(int id);
        }

        [System.CodeDom.Compiler.GeneratedCode("NSwag", "test")]
        public partial interface IRegistrationFormsClient
        {
            void CreateRegistrationForm(RegistrationRequestDto body);
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class EventRequestDto
        {
            public NestedInputDto? Nested { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class RegistrationRequestDto
        {
            public string? Value { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class NestedInputDto
        {
            public string? Value { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class SharedResponseDto
        {
            public string? Value { get; set; }
        }
        }
        """;

    private const string ClientlessGeneratedSource =
        """
        #nullable enable

        namespace Generated
        {
        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class OrphanDto
        {
            public string? Value { get; set; }
        }
        }
        """;

    private const string GeneratedSource =
        """
        #nullable enable

        namespace Generated
        {
        [System.CodeDom.Compiler.GeneratedCode("NSwag", "test")]
        public partial interface ISampleClient
        {
            void Send(RequestDto body);
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class RequestDto
        {
            public NestedInputDto? Nested { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class NestedInputDto
        {
            public string? Value { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class ResponseDto
        {
            public string? Value { get; set; }

            [System.Text.Json.Serialization.JsonExtensionData]
            public System.Collections.Generic.IDictionary<string, object>? AdditionalProperties { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class NestedResponseDto
        {
            public ResponseDto? Nested { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class HalEnvelopeDto
        {
            public ResponseDto? Value { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class BaseDto
        {
            public string? Value { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class DerivedDto : BaseDto
        {
            public string? Other { get; set; }
        }

        [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "test")]
        public partial class MutableViewModel
        {
            public string? Value { get; set; }
        }
        }
        """;
}
