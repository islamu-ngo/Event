namespace ISLAMU.Event.SetupAssistant.Presentation;

using System.Globalization;

public readonly record struct SetupOperationGeneration(long Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public interface ISetupOperationGenerationAllocator
{
    bool TryAllocate(out SetupOperationGeneration generation);
}

public sealed class SetupOperationGenerationAllocator : ISetupOperationGenerationAllocator
{
    private long _current;

    public bool TryAllocate(out SetupOperationGeneration generation)
    {
        while (true)
        {
            long current = Volatile.Read(ref _current);
            if (current == long.MaxValue)
            {
                generation = default;
                return false;
            }

            long next = current + 1;
            if (Interlocked.CompareExchange(ref _current, next, current) == current)
            {
                generation = new SetupOperationGeneration(next);
                return true;
            }
        }
    }
}
