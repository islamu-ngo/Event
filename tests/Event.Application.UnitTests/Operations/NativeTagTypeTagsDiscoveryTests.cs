using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.TagType;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Handlers.Commands;
using Explore.Application.Features.TagTypeTags.Handlers.Queries;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeTagTypeTagsDiscoveryTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersAllEightCorrectlyClassifiedPorts()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateTagTypeTagsCommand), typeof(CreateTagTypeTagsCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateTagTypeTagsCommand), typeof(UpdateTagTypeTagsCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteTagTypeTagsCommand), typeof(DeleteTagTypeTagsCommandHandler),
                typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(GetTagsByTagTypeRequest), typeof(GetTagsByTagTypeRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<TagListDto>)),
            (typeof(GetTagsGroupedByTagTypeRequest), typeof(GetTagsGroupedByTagTypeRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<TagTypeWithTagsDto>)),
            (typeof(GetTagTypeTagsDetailsRequest), typeof(GetTagTypeTagsDetailsRequestHandler),
                typeof(IQueryHandler<,>), typeof(TagTypeTagsDto)),
            (typeof(GetTagTypeTagsListRequest), typeof(GetTagTypeTagsListRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<TagTypeTagsListDto>)),
            (typeof(GetTagTypesForTagRequest), typeof(GetTagTypesForTagRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<TagTypeListDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(8);
        foreach (var operation in operations)
        {
            var port = ports.Single(type => type.GetGenericArguments()[0] == operation.Request);
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }

    [Test]
    public async Task MissingRelationship_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetTagTypeTagsDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<TagTypeTagsDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
