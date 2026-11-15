#:property ManagePackageVersionsCentrally=false
#:property RestorePackagesWithLockFile=true
#:property NuGetLockFilePath=InstallAssuranceBrowser/packages.lock.json
#:property RestoreLockedMode=true
#:package Microsoft.Playwright@1.64.1

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet run eng/tools/InstallAssuranceBrowser.cs");
    return 2;
}

return Microsoft.Playwright.Program.Main(["install", "chromium", "--only-shell"]);
