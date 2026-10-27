using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitBorrowedOperationTests
{
    [Test]
    public async Task RetirementCancelsBorrowedWorkAndDrainsBeforeDisposal()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var operation = owner.BeginOperation();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = operation.Token.Register(() => cancelled.TrySetResult());
        Task retirement = owner.DisposeAsync().AsTask();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(retirement.IsCompleted).IsFalse();
            await Assert.That(() => owner.BeginOperation()).Throws<ObjectDisposedException>();
        }
        finally
        {
            operation.Dispose();
        }
        await retirement.WaitAsync(TimeSpan.FromSeconds(10));
        operation.Dispose();
    }
}
