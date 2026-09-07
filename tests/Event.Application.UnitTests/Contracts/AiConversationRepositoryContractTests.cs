using Explore.Application.Contracts.Persistence;
using TUnit.Core;

namespace Event.Application.UnitTests.Contracts;

[Category("AiConversation")]
public sealed class AiConversationRepositoryContractTests
{
    [Test]
    public async Task HardDeleteGraph_ReturnsTaskIntAndNotQueryable()
    {
        var method = typeof(IAiConversationRepository).GetMethod(
            nameof(IAiConversationRepository.HardDeleteUserConversationGraphAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task<int>));
        await Assert.That(typeof(System.Linq.IQueryable).IsAssignableFrom(method.ReturnType)).IsFalse();
    }
}
