using System.Runtime.CompilerServices;
using VerifyTests;

namespace Event.Api.IntegrationTests;

public static class VerifySnapshotSettings
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UseStrictJson();
    }
}
