using Explore.Application.Contracts.Operations;
using Explore.Application.Models;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record TestInstanceSmtpConnectionQuery : IQuery<EmailResult>
{
}
