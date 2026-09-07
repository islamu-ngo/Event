// ABOUTME: Application boundary for testing the instance email provider connection.
// ABOUTME: Keeps SMTP client and transport details outside controllers, health checks, and handlers.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IEmailConnectionTester
{
    /// <summary>
    /// Tests instance SMTP and returns network/authentication outcomes as results.
    /// Escaping exceptions indicate configuration authority or diagnostic failures, not ordinary SMTP unavailability.
    /// </summary>
    Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
