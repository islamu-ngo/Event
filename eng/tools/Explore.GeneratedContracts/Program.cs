namespace Explore.GeneratedContracts.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: Explore.GeneratedContracts <generated-client.cs> <mutable-contracts.txt>");
            return 2;
        }

        try
        {
            TransformResult result =
                GeneratedContractTransformer.TransformFile(
                    args[0],
                    args[1]);
            Console.WriteLine(
                "Generated record policy: {0} records, {1} init accessors, changed={2}.",
                result.RecordCount,
                result.InitAccessorCount,
                result.Changed);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
