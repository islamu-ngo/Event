using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Contracts.Secrets;

public sealed class SecretResolutionResultTests
{
    [Test]
    public async Task ResultStates_ExposeMaterialOnlyWhenResolved()
    {
        string value = $"secret-{Guid.CreateVersion7():N}";
        var secret = new ResolvedSecret(
            "test.secret",
            value,
            SecretSourceType.EnvironmentVariable,
            SecretScope.Instance,
            ScopeId: null,
            DateTimeOffset.UtcNow);
        SecretResolutionResult resolved = SecretResolutionResult.Resolved(secret);
        SecretResolutionResult[] failures =
        [
            SecretResolutionResult.Unconfigured,
            SecretResolutionResult.Unavailable,
            SecretResolutionResult.Unauthorized,
            SecretResolutionResult.Invalid,
        ];

        await Assert.That(resolved.Status).IsEqualTo(SecretResolutionStatus.Resolved);
        await Assert.That(resolved.Secret).IsSameReferenceAs(secret);
        await Assert.That(resolved.ToString()).DoesNotContain(value);
        await Assert.That(failures.All(result => result.Secret is null && !result.IsResolved)).IsTrue();
    }
}
