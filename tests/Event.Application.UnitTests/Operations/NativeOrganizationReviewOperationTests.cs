using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;
using Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;
using Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationReviewOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersOneCommandAndTwoQueries()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateOrganizationReviewCommand), typeof(CreateOrganizationReviewCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetMyReviewsQuery), typeof(GetMyReviewsQueryHandler),
                typeof(IQueryHandler<,>), typeof(List<OrganizationReviewDto>)),
            (typeof(GetOrganizationReviewsQuery), typeof(GetOrganizationReviewsQueryHandler),
                typeof(IQueryHandler<,>), typeof(List<OrganizationReviewDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(
            operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(3);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }
}
