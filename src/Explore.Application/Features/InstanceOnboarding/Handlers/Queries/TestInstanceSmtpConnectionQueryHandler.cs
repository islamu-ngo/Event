using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class TestInstanceSmtpConnectionQueryHandler(IEmailConnectionTester connectionTester)
    : IRequestHandler<TestInstanceSmtpConnectionQuery, EmailResult>
{
    public async Task<EmailResult> Handle(
        TestInstanceSmtpConnectionQuery request,
        CancellationToken cancellationToken)
    {
        return await connectionTester.TestConnectionAsync(cancellationToken);
    }
}
