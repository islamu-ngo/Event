using TUnit.Core;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Event.Api.IntegrationTests.ApiTestParallelLimit>]

namespace Event.Api.IntegrationTests;

public sealed class ApiTestParallelLimit : IParallelLimit
{
    public int Limit => 8;
}
