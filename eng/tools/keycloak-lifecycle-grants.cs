#:project ../../src/Explore.Domain/Explore.Domain.csproj

using System.Text.Json;
using Explore.Domain.Keycloak;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: keycloak-lifecycle-grants <provider-version> <realm> <client-id> <realm-export-file>");
    return 2;
}

try
{
    await using var input = File.OpenRead(args[3]);
    if (input.Length > 8 * 1024 * 1024)
    {
        Console.Error.WriteLine("keycloak_lifecycle_grant_profile_invalid");
        return 1;
    }
    using var export = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { MaxDepth = 64 });
    bool accepted = KeycloakLifecycleGrantProfile.AllowsDeployment(export.RootElement, args[0], args[1], args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new { valid = accepted }));
    return accepted ? 0 : 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine("keycloak_lifecycle_grant_profile_invalid");
    return 1;
}
