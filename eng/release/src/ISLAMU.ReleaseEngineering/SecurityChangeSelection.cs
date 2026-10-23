using System.Text;

namespace ISLAMU.ReleaseEngineering;

public static class SecurityChangeSelection
{
    public static bool RequiresExecution(IEnumerable<string> changedPaths)
    {
        bool anyPath = false;
        foreach (string path in changedPaths)
        {
            anyPath = true;
            // Only known public prose can skip; new or ambiguous paths must run.
            if (string.IsNullOrWhiteSpace(path)
                || path != path.Trim()
                || path.Any(char.IsControl)
                || path.Contains('\\', StringComparison.Ordinal)
                || path.Split('/').Any(segment => segment is "" or "." or "..")
                || !(path == "README.md"
                    || (path.StartsWith("docs/public/", StringComparison.Ordinal)
                        && path.EndsWith(".md", StringComparison.Ordinal))))
            {
                return true;
            }
        }

        return !anyPath;
    }

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 2)
        {
            output.WriteLine("run-tests=true");
            return Program.Success;
        }

        bool selected;
        try
        {
            selected = RequiresExecution(File.ReadLines(args[1], new UTF8Encoding(false, true)));
        }
        catch (IOException)
        {
            selected = true;
        }
        catch (UnauthorizedAccessException)
        {
            selected = true;
        }
        catch (ArgumentException)
        {
            selected = true;
        }

        output.WriteLine(selected ? "run-tests=true" : "run-tests=false");
        return Program.Success;
    }
}
