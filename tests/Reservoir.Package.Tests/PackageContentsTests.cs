using System.IO.Compression;

namespace Reservoir.Package.Tests;

public class PackageContentsTests
{
    [Test]
    public async Task PackageContainsRuntimeAssetsAndDocumentationWithoutSourceDelivery()
    {
        string packagePath = Environment.GetEnvironmentVariable("RESERVOIR_PACKAGE_PATH")
            ?? throw new InvalidOperationException("Set RESERVOIR_PACKAGE_PATH to the package under test.");
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package.Entries.Select(entry => entry.FullName).ToArray();

        foreach (string target in new[] { "netstandard2.0", "net8.0", "net10.0" })
        {
            await Assert.That(entries).Contains($"lib/{target}/Reservoir.dll");
            await Assert.That(entries).Contains($"lib/{target}/Reservoir.xml");
        }

        foreach (string entry in entries)
        {
            await Assert.That(entry.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase)
                || entry.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
                .IsFalse().Because($"Package must not contain obsolete source-delivery asset {entry}.");
        }
    }
}
