using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IEmailConnectionTester
{
    Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
