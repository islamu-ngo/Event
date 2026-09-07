namespace Explore.Application.Contracts.Secrets;

public interface ISecretAuthorityStatusReader
{
    Task<SecretAuthorityStatusSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record SecretAuthorityStatusSnapshot(
    string Provider,
    string Status,
    string RemediationCode);
