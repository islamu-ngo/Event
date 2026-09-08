using Explore.Application.Models;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record TestInstanceSmtpConnectionQuery : IRequest<EmailResult>
{
}
