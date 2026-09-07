using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class StorageUploadOpenApiContractTests
{
    [Test]
    public async Task StorageMetadataContracts_MustNotExposeProviderObjectKeys()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        string[] storageMetadataSchemas =
        [
            "StorageObjectDto",
            "StorageObjectListDto",
            "HalResourceOfStorageObjectDto",
            "HalResourceOfStorageObjectListDto"
        ];

        foreach (var schemaName in storageMetadataSchemas)
        {
            await Assert.That(schemas.GetProperty(schemaName).GetProperty("properties").TryGetProperty("objectKey", out _)).IsFalse();
        }

        var generatedClient = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));

        string[] generatedStorageMetadataTypes =
        [
            "HalResourceOfStorageObjectDto",
            "HalResourceOfStorageObjectListDto",
            "StorageObjectDto",
            "StorageObjectListDto"
        ];

        foreach (var typeName in generatedStorageMetadataTypes)
        {
            int typeStart = FindGeneratedTypeStart(
                generatedClient,
                typeName);
            await Assert.That(typeStart).IsGreaterThanOrEqualTo(0);
            var typeEnd = generatedClient.IndexOf("\n    [System.CodeDom.Compiler.GeneratedCode", typeStart + 1, StringComparison.Ordinal);
            var generatedType = typeEnd >= 0 ? generatedClient[typeStart..typeEnd] : generatedClient[typeStart..];

            await Assert.That(generatedType).DoesNotContain("ObjectKey");
        }
    }

    private static int FindGeneratedTypeStart(string generated, string typeName)
    {
        int recordStart = generated.IndexOf(
            $"partial record class {typeName}",
            StringComparison.Ordinal);
        return recordStart >= 0
            ? recordStart
            : generated.IndexOf(
                $"partial class {typeName}",
                StringComparison.Ordinal);
    }

    [Test]
    public async Task StorageUploadSessionContent_MustDeclareRequiredBinaryRequestBody()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/storageobject/upload-sessions/{uploadSessionId}/content")
            .GetProperty("put");
        var requestBody = operation.GetProperty("requestBody");
        var schema = requestBody
            .GetProperty("content")
            .GetProperty("application/octet-stream")
            .GetProperty("schema");

        await Assert.That(requestBody.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(schema.GetProperty("format").GetString()).IsEqualTo("binary");
    }

    [Test]
    public async Task LegacyStorageMutationArtifacts_MustRemainAbsent()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);
        var paths = document.RootElement.GetProperty("paths");

        await Assert.That(paths.TryGetProperty("/api/storageobject/generate-upload-url", out _)).IsFalse();
        await Assert.That(paths.GetProperty("/api/storageobject").TryGetProperty("post", out _)).IsFalse();
        await Assert.That(paths.TryGetProperty("/api/storageobject/upload-sessions", out _)).IsTrue();

        string[] obsoleteSourceFiles =
        [
            "src/Explore.Application/Features/StorageObjects/Requests/Commands/GenerateUploadUrlCommand.cs",
            "src/Explore.Application/Features/StorageObjects/Requests/Commands/CreateStorageObjectCommand.cs",
            "src/Explore.Application/Features/StorageObjects/Handlers/Commands/GenerateUploadUrlCommandHandler.cs",
            "src/Explore.Application/Features/StorageObjects/Handlers/Commands/CreateStorageObjectCommandHandler.cs",
            "src/Explore.Application/DTOs/StorageObject/UploadRequestDto.cs",
            "src/Explore.Application/DTOs/StorageObject/UploadUrlResponseDto.cs",
            "src/Explore.Application/DTOs/StorageObject/CreateStorageObjectDto.cs",
            "src/Explore.Blazor.Client/Services/ImageStorageRecordClient.cs",
            "src/Explore.Blazor.Client/Services/Http/DirectStorageUploadMessageHandler.cs",
            "src/Explore.Blazor.Client/Services/StorageHttpClientNames.cs"
        ];

        foreach (var relativePath in obsoleteSourceFiles)
        {
            await Assert.That(File.Exists(Path.Combine(repositoryRoot, relativePath))).IsFalse();
        }

        var generatedClient = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));
        string[] obsoleteGeneratedSymbols =
        [
            "CreateStorageObjectAsync",
            "GenerateStorageObjectUploadUrlAsync",
            "CreateStorageObjectDto",
            "UploadRequestDto",
            "UploadUrlResponseDto"
        ];

        foreach (var symbol in obsoleteGeneratedSymbols)
        {
            await Assert.That(generatedClient).DoesNotContain(symbol);
        }
    }

    [Test]
    public async Task InfrastructureRasterGateway_MustUseOnlyApplicationPolicy()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var infrastructureRoot = Path.Combine(repositoryRoot, "src", "Explore.Infrastructure");
        var gateway = await File.ReadAllTextAsync(Path.Combine(
            infrastructureRoot,
            "Services",
            "Federation",
            "AtprotoThumbnailBlobGateway.cs"));

        await Assert.That(gateway).Contains("SafeRasterContentPolicy.TryNormalizeMimeType");
        await Assert.That(gateway).Contains("SafeRasterContentPolicy.MatchesContainer");

        string[] privateParserSymbols =
        [
            "IsJpegContainer",
            "IsPngContainer",
            "IsGifContainer",
            "IsWebpContainer",
            "IsAvifContainer",
            "JpegSignature",
            "PngSignature",
            "Vp8KeyFrameSignature"
        ];
        var infrastructureSources = Directory
            .EnumerateFiles(infrastructureRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var symbol in privateParserSymbols)
        {
            await Assert.That(infrastructureSources.Any(source => source.Contains(symbol, StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task BrowserStorageMutations_MustRemainUploadSessionBased()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var imageUploadClient = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Services",
            "ImageUploadClient.cs"));
        var bffStorageEndpoints = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "Extensions",
            "BffStorageEndpoints.cs"));

        await Assert.That(imageUploadClient).Contains("/bff/storage/upload-session");
        await Assert.That(imageUploadClient).Contains("/bff/storage/upload-proxy");
        await Assert.That(imageUploadClient).DoesNotContain("HttpMethod.Put");
        await Assert.That(imageUploadClient).DoesNotContain(".PutAsync(");
        await Assert.That(bffStorageEndpoints).Contains("CreateStorageUploadSessionAsync");
        await Assert.That(bffStorageEndpoints).Contains("UploadStorageUploadSessionContentAsync");

        string[] obsoleteSymbols =
        [
            "CreateStorageObjectAsync",
            "GenerateStorageObjectUploadUrlAsync",
            "ImageStorageRecordClient",
            "DirectStorageUploadMessageHandler",
            "StorageHttpClientNames"
        ];

        foreach (var symbol in obsoleteSymbols)
        {
            await Assert.That(imageUploadClient).DoesNotContain(symbol);
            await Assert.That(bffStorageEndpoints).DoesNotContain(symbol);
        }
    }

    [Test]
    public async Task StaticSvgPresentationClassifiers_MustStayOutsideUploadAllowlists()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var manifestEndpoints = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "Extensions",
            "BffManifestEndpoints.cs"));
        var imageClassifier = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Services",
            "ImageContentClassifier.cs"));
        var imageUploadPolicy = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Services",
            "ImageUploadClientPolicy.cs"));
        var bffStorageEndpoints = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "Extensions",
            "BffStorageEndpoints.cs"));

        await Assert.That(manifestEndpoints).Contains("\".svg\" => \"image/svg+xml\"");
        await Assert.That(imageClassifier).Contains("\"image/svg+xml\" => \".svg\"");
        await Assert.That(imageUploadPolicy).DoesNotContain("image/svg+xml");
        await Assert.That(bffStorageEndpoints).DoesNotContain("image/svg+xml");
    }

    [Test]
    public async Task ProviderOperations_MustRetainSafeServerOwnedCallers()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var objectStorageService = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Infrastructure",
            "Services",
            "ObjectStorageService.cs"));
        var presignedDownloadHandler = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Application",
            "Features",
            "StorageObjects",
            "Handlers",
            "Queries",
            "GetPresignedDownloadUrlRequestHandler.cs"));
        var finalizeHandler = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Application",
            "Features",
            "StorageObjects",
            "Handlers",
            "Commands",
            "FinalizeStorageUploadSessionCommandHandler.cs"));
        var deletionService = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Infrastructure",
            "StorageObjectDeletionService.cs"));
        var reconciliationService = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Infrastructure",
            "StorageReconciliationService.cs"));

        await Assert.That(objectStorageService).Contains("GeneratePresignedDownloadUrl");
        await Assert.That(objectStorageService).DoesNotContain("GeneratePresignedUploadUrl");
        await Assert.That(presignedDownloadHandler).Contains("GeneratePresignedDownloadUrl");
        await Assert.That(finalizeHandler).Contains("provider.WriteAsync");
        await Assert.That(deletionService).Contains("provider.DeleteAsync");
        await Assert.That(reconciliationService).Contains("ListKnownObjectKeysAsync");
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
