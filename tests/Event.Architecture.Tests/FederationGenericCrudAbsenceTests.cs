namespace Event.Architecture.Tests;

using System.Text.Json;

public sealed class FederationGenericCrudAbsenceTests
{
    [Test]
    public async Task GenericProviderStateCrudSurfacesMustRemainAbsent()
    {
        var root = ResolveRepositoryRoot();
        string[] retiredFiles =
        [
            "src/Explore.API/Controllers/ActorKeyStoreController.cs",
            "src/Explore.API/Controllers/SyncStateController.cs",
            "src/Explore.API/Controllers/IndexedDidController.cs",
            "src/Explore.API/Controllers/UserExternalLoginController.cs",
            "src/Explore.Application/DTOs/ActorKeyStore/UpdateActorKeyStoreDto.cs",
            "src/Explore.Application/DTOs/SyncState/UpdateSyncStateDto.cs",
            "src/Explore.Application/DTOs/IndexedDid/UpdateIndexedDidDto.cs",
            "src/Explore.Application/DTOs/UserExternalLogin/UpdateUserExternalLoginDto.cs",
            "src/Explore.Application/Features/ActorKeyStores/Handlers/Commands/UpdateActorKeyStoreCommandHandler.cs",
            "src/Explore.Application/Features/SyncStates/Handlers/Commands/UpdateSyncStateCommandHandler.cs",
            "src/Explore.Application/Features/IndexedDids/Handlers/Commands/UpdateIndexedDidCommandHandler.cs",
            "src/Explore.Application/Features/UserExternalLogins/Handlers/Commands/UpdateUserExternalLoginCommandHandler.cs"
        ];

        foreach (var relativePath in retiredFiles)
        {
            await Assert.That(File.Exists(Path.Combine(root, relativePath))).IsFalse();
        }

        string[] retainedAuthorityFiles =
        [
            "src/Explore.Application/Features/Authentication/Atproto/Handlers/Commands/BootstrapAtprotoSessionCommandHandler.cs",
            "src/Explore.Persistence/Configurations/Entities/Federation/AtprotoJetstreamConsumerStateConfiguration.cs"
        ];

        foreach (var relativePath in retainedAuthorityFiles)
        {
            await Assert.That(File.Exists(Path.Combine(root, relativePath))).IsTrue();
        }

        var openApi = await File.ReadAllTextAsync(Path.Combine(root, "schemas", "openapi_islamu-event.json"));
        await Assert.That(openApi).DoesNotContain("/api/actorkeystore");
        await Assert.That(openApi).DoesNotContain("/api/syncstate");
        await Assert.That(openApi).DoesNotContain("/api/indexeddid");
        await Assert.That(openApi).DoesNotContain("/api/userexternallogin");

        using var document = JsonDocument.Parse(openApi);
        var paths = document.RootElement.GetProperty("paths");
        var actorCollection = paths.GetProperty("/api/actor");
        var actorDetail = paths.GetProperty("/api/actor/{id}");
        await Assert.That(actorCollection.TryGetProperty("post", out _)).IsFalse();
        await Assert.That(actorDetail.TryGetProperty("patch", out _)).IsFalse();
        await Assert.That(actorDetail.TryGetProperty("delete", out _)).IsFalse();

        var generatedClient = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));
        await Assert.That(generatedClient).DoesNotContain("CreateActorAsync(");
        await Assert.That(generatedClient).DoesNotContain("UpdateActorAsync(");
        await Assert.That(generatedClient).DoesNotContain("DeleteActorAsync(");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }
}
