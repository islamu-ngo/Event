namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    internal static async Task<AuthenticatedSettingsFixture> CreateSqliteAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"authenticated-settings-{Guid.CreateVersion7():N}.db");
        try
        {
            await EmailDispatchSqliteFixture.CreateDatabaseAsync(path);
            var fixture = await CreateAsync(() => EmailDispatchSqliteFixture.CreateContext(path));
            fixture.ownedSqlitePath = path;
            return fixture;
        }
        catch
        {
            DeleteSqlite(path);
            throw;
        }
    }
}
