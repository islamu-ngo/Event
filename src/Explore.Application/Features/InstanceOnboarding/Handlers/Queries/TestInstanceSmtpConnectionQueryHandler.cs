using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class TestInstanceSmtpConnectionQueryHandler(IEmailConnectionTester connectionTester)
    : IQueryHandler<TestInstanceSmtpConnectionQuery, EmailResult>
{
    public async Task<EmailResult> QueryAsync(
        TestInstanceSmtpConnectionQuery request,
        CancellationToken cancellationToken)
    {
        return await connectionTester.TestConnectionAsync(cancellationToken);
    }
}
