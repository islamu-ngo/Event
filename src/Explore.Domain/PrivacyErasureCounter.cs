namespace Explore.Domain;

public sealed class PrivacyErasureCounter
{
    private PrivacyErasureCounter()
    {
    }

    public bool Singleton { get; private set; }
    public long LastSequence { get; private set; }
    public long RetainedFloorSequence { get; private set; }

    public static PrivacyErasureCounter Start() => new()
    {
        Singleton = true,
        LastSequence = 0,
        RetainedFloorSequence = 0
    };

    public long AllocateNext()
    {
        if (LastSequence == long.MaxValue)
        {
            throw new InvalidOperationException("The erasure-ledger sequence is exhausted.");
        }

        return ++LastSequence;
    }

    public void AdvanceTo(long sequence)
    {
        if (sequence != LastSequence + 1)
        {
            throw new InvalidOperationException("Mirrored erasure facts must advance the ledger by one.");
        }

        LastSequence = sequence;
    }

    public void AdvanceRetainedFloorTo(long sequence)
    {
        if (sequence < RetainedFloorSequence || sequence > LastSequence)
        {
            throw new InvalidOperationException("The retained floor must advance monotonically within the high-water mark.");
        }

        RetainedFloorSequence = sequence;
    }

    public PrivacyErasureAuthorityState GetState() =>
        new(LastSequence, RetainedFloorSequence);
}
