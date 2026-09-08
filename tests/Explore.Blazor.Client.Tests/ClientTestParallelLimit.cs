using TUnit.Core;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Explore.Blazor.Client.Tests.ClientTestParallelLimit>]

namespace Explore.Blazor.Client.Tests;

public sealed class ClientTestParallelLimit : IParallelLimit
{
    public int Limit => 8;
}
