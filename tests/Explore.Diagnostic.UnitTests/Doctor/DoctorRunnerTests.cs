using Explore.Diagnostic.Doctor;

namespace Explore.Diagnostic.UnitTests.Doctor;

public class DoctorRunnerTests
{
    [Test]
    public async Task RunAsync_WhenCheckThrows_ReturnsWarnResultWithRedactedEvidence()
    {
        var runner = new DoctorRunner([new ThrowingCheck()]);

        var report = await runner.RunAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        await Assert.That(report.Results.Count).IsEqualTo(1);
        var result = report.Results[0];
        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Warn);
        await Assert.That(result.RedactedEvidence).Contains("Password=<redacted>");
        await Assert.That(result.RedactedEvidence).DoesNotContain("super-secret");
    }

    private sealed class ThrowingCheck : IDoctorCheck
    {
        public string Code => "test.throwing";
        public DoctorCheckCategory Category => DoctorCheckCategory.Configuration;

        public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Password=super-secret");
    }
}
