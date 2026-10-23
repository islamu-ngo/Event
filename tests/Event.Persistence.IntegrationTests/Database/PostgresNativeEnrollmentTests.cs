using Explore.Persistence.Security;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PostgresNativeEnrollmentTests
{
    [Test]
    public void FiveDistinctEnrolledIdentitiesRemainValid()
    {
        new PostgresNativeQueueEnrollment("migrator", "global", "runtime", "email_instance", "email_tenant").Validate();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(0, 2)]
    [Arguments(0, 3)]
    [Arguments(0, 4)]
    [Arguments(1, 2)]
    [Arguments(1, 3)]
    [Arguments(1, 4)]
    [Arguments(2, 3)]
    [Arguments(2, 4)]
    [Arguments(3, 4)]
    public async Task NoTwoEnrolledAuthoritiesCanShareAnIdentity(int first, int second)
    {
        string[] logins = ["migrator", "global", "runtime", "email_instance", "email_tenant"];
        logins[second] = logins[first];
        await RejectAsync(logins);
    }

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    public async Task EmailAuthoritiesUseTheSamePostgresIdentifierBoundary(int index)
    {
        foreach (string invalid in new[] { "", " login", "login ", "login\n", new string('a', 64), new string('\u00e9', 32) })
        {
            string[] logins = ["migrator", "global", "runtime", "email_instance", "email_tenant"];
            logins[index] = invalid;
            await RejectAsync(logins);
        }
        string[] valid = ["migrator", "global", "runtime", "email_instance", "email_tenant"];
        valid[index] = new string('a', 63);
        Create(valid).Validate();
        valid[index] = new string('\u00e9', 31) + "a";
        Create(valid).Validate();
    }

    private static async Task RejectAsync(string[] logins)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            Create(logins).Validate();
            return Task.CompletedTask;
        });
        await Assert.That(error!.Message).IsEqualTo("native_queue_login_enrollment_invalid");
    }

    private static PostgresNativeQueueEnrollment Create(string[] logins) =>
        new(logins[0], logins[1], logins[2], logins[3], logins[4]);
}
