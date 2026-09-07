// for improved database index performance compared to random UUID v4.

namespace Explore.Persistence.ValueGenerators;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

public class GuidVersion7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry)
    {
        return Guid.CreateVersion7();
    }
}
