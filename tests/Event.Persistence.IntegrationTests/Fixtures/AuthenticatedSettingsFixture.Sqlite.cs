namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    internal static async Task<AuthenticatedSettingsFixture> CreateSqliteAsync(
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null,
        bool embeddedPrivacyAuthority = false)
    {
        string path = Path.Combine(Path.GetTempPath(), $"authenticated-settings-{Guid.CreateVersion7():N}.db");
        string? authorityDirectory = embeddedPrivacyAuthority
            ? Directory.CreateTempSubdirectory("learning-authority-").FullName : null;
        try
        {
            await EmailDispatchSqliteFixture.CreateDatabaseAsync(path);
            var fixture = await CreateCoreAsync(
                () => EmailDispatchSqliteFixture.CreateContext(path), null, configurePostgres: null,
                configure: configuration =>
                {
                    if (embeddedPrivacyAuthority)
                    {
                        configuration["PrivacyErasureAuthorityEmbedded:Path"] =
                            Path.Combine(authorityDirectory!, "authority.db");
                        configuration["Testing:UseRealDatabase"] = "true";
                    }
                },
                configureServices: configureServices,
                privacyAuthorityTopology: embeddedPrivacyAuthority
                    ? Explore.Application.Configuration.PrivacyErasureAuthorityTopology.EmbeddedSqlite
                    : Explore.Application.Configuration.PrivacyErasureAuthorityTopology.CoLocated);
            fixture.ownedSqlitePath = path;
            fixture.ownedAuthorityDirectory = authorityDirectory;
            return fixture;
        }
        catch
        {
            DeleteSqlite(path);
            if (authorityDirectory is not null)
            {
                DeleteSqlite(Path.Combine(authorityDirectory, "authority.db"));
                Directory.Delete(authorityDirectory, recursive: true);
            }
            throw;
        }
    }
}
