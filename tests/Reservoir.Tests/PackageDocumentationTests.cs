using System.Diagnostics;
using System.IO.Compression;

namespace Reservoir.Tests;

public class PackageDocumentationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("netstandard2.0")]
    [Arguments("net8.0")]
    [Arguments("net10.0")]
    public async Task PackageValidationRequiresDocumentationForEveryTarget(string? missingTarget)
    {
        string packagePath = Path.Combine(Path.GetTempPath(), $"Reservoir.PackageDocs.{Guid.NewGuid():N}.nupkg");
        try
        {
            using (ZipArchive package = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                foreach (string target in new[] { "netstandard2.0", "net8.0", "net10.0" })
                {
                    package.CreateEntry($"lib/{target}/Reservoir.dll");
                    if (target != missingTarget)
                    {
                        package.CreateEntry($"lib/{target}/Reservoir.xml");
                    }
                }
            }

            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "build", "Test-PackageContents.ps1")))
            {
                repository = repository.Parent;
            }

            if (repository is null)
            {
                throw new InvalidOperationException("Cannot locate the package validation script.");
            }

            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(Path.Combine(repository.FullName, "build", "Test-PackageContents.ps1"));
            start.ArgumentList.Add("-PackagePath");
            start.ArgumentList.Add(packagePath);
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            string diagnostics = await output + await error;
            if (missingTarget is null)
            {
                await Assert.That(process.ExitCode).IsEqualTo(0);
            }
            else
            {
                await Assert.That(process.ExitCode).IsNotEqualTo(0);
                await Assert.That(diagnostics).Contains($"lib/{missingTarget}/Reservoir.xml");
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }
}
