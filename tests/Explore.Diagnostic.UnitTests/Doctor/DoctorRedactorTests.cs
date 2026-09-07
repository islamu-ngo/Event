using Explore.Diagnostic.Doctor;

namespace Explore.Diagnostic.UnitTests.Doctor;

public class DoctorRedactorTests
{
    [Test]
    public async Task Redact_RemovesSensitiveKeyValues()
    {
        var redacted = DoctorRedactor.Redact("Host=db;Password=s3cr3t Token=abc Authorization=Bearer xyz");

        await Assert.That(redacted).Contains("Password=<redacted>");
        await Assert.That(redacted).Contains("Token=<redacted>");
        await Assert.That(redacted).Contains("Authorization=<redacted>");
        await Assert.That(redacted).DoesNotContain("s3cr3t");
        await Assert.That(redacted).DoesNotContain("abc");
        await Assert.That(redacted).DoesNotContain("Bearer");
    }

    [Test]
    public async Task Redact_RemovesUriCredentials()
    {
        var redacted = DoctorRedactor.Redact("postgres://user:password@database:5432/explore");

        await Assert.That(redacted).IsEqualTo("postgres://<redacted>@database:5432/explore");
    }
}
