using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNativeAuthorityParticipantTests
{
    internal sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
            string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }
}
