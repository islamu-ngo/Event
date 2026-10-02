#:property RestorePackagesWithLockFile=false

using System.Xml.Linq;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run eng/coverage/validate-reports.cs -- <report-directory>");
    return 1;
}

string[] reports = Directory.GetFiles(args[0], "*.cobertura.xml");
if (reports.Length == 0)
{
    Console.Error.WriteLine("No Cobertura reports were generated.");
    return 1;
}

HashSet<string> productAssemblies = Directory.EnumerateFiles("src", "*.csproj", SearchOption.AllDirectories)
    .Select(path => Path.GetFileNameWithoutExtension(path))
    .Where(name => !name.Contains("Migrations", StringComparison.Ordinal)
        && name != "Explore.AppHost")
    .ToHashSet(StringComparer.Ordinal);

bool valid = true;
HashSet<string> measuredAssemblies = new(StringComparer.Ordinal);
foreach (string report in reports)
{
    XElement root = XElement.Load(report);
    XElement[] packages = root.Element("packages")?.Elements("package").ToArray() ?? [];
    int lines = packages.SelectMany(package => package.Descendants("class"))
        .Sum(type => type.Element("lines")?.Elements("line").Count() ?? 0);
    if (packages.Length == 0 || lines == 0)
    {
        Console.Error.WriteLine($"{report}: contains no product coverage lines.");
        valid = false;
        continue;
    }

    foreach (XElement package in packages)
    {
        string assembly = (string?)package.Attribute("name") ?? "";
        if (!productAssemblies.Contains(assembly))
        {
            Console.Error.WriteLine($"{report}: unexpected assembly '{assembly}'.");
            valid = false;
        }
        measuredAssemblies.Add(assembly);
        foreach (XElement type in package.Descendants("class"))
        {
            string source = ((string?)type.Attribute("filename") ?? "").Replace('\\', '/');
            string filename = Path.GetFileName(source);
            if (new[] { "/tests/", "/eng/", "/obj/", "/bin/", "/Migrations/", "/Generated/" }
                .Any(segment => source.Contains(segment, StringComparison.OrdinalIgnoreCase))
                || new[] { ".g.cs", ".generated.cs", ".designer.cs", "ModelSnapshot.cs" }
                .Any(suffix => filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"{report}: excluded source '{source}' was collected.");
                valid = false;
            }
        }
    }

    Console.WriteLine($"{report}: {string.Join(", ", packages.Select(package => (string?)package.Attribute("name")))}; {lines} coverable line entries.");
}

Console.WriteLine($"Product assemblies without collected coverage: {string.Join(", ", productAssemblies.Except(measuredAssemblies).Order(StringComparer.Ordinal))}");
return valid ? 0 : 1;
