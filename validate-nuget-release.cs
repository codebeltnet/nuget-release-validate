using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal static class Program
{
    private const string NuGetFlatContainer = "https://api.nuget.org/v3-flatcontainer";
    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?$",
        RegexOptions.CultureInvariant);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                throw new InvalidDataException("Expected package directory and release version arguments.");
            }

            var packageDirectory = Path.GetFullPath(args[0]);
            var expectedVersion = args[1];
            if (!SemVerPattern.IsMatch(expectedVersion))
            {
                throw new InvalidDataException($"Release version '{expectedVersion}' is not a supported SemVer value.");
            }
            if (!Directory.Exists(packageDirectory))
            {
                throw new DirectoryNotFoundException($"NuGet package directory was not found: {packageDirectory}");
            }

            var packages = Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(path => ReadPackage(path, expectedVersion))
                .ToArray();
            if (packages.Length == 0)
            {
                throw new InvalidDataException($"No NuGet packages were produced in '{packageDirectory}'.");
            }

            using var concurrencyGate = new SemaphoreSlim(8);
            var results = await Task.WhenAll(packages.Select(async package =>
            {
                await concurrencyGate.WaitAsync();
                try
                {
                    return await CheckExistingPackageAsync(package);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            }));

            var identical = results.Count(result => result.State == PackageState.Identical);
            var newPackages = results.Length - identical;
            WriteOutputs(results.Length, newPackages, identical);

            Console.WriteLine(
                $"Validated {results.Length} package(s): {newPackages} new and {identical} already published with matching SHA-512.");
            return 0;
        }
        catch (Exception exception)
        {
            var message = exception.Message
                .Replace("%", "%25", StringComparison.Ordinal)
                .Replace("\r", "%0D", StringComparison.Ordinal)
                .Replace("\n", "%0A", StringComparison.Ordinal);
            Console.Error.WriteLine($"::error::{message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Codebelt-NuGet-Release-Validation", "1.0"));
        return client;
    }

    private static NuGetPackage ReadPackage(string path, string expectedVersion)
    {
        using var packageStream = File.OpenRead(path);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var nuspecEntries = archive.Entries
            .Where(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                && !entry.FullName.Contains('/', StringComparison.Ordinal)
                && !entry.FullName.Contains('\\', StringComparison.Ordinal))
            .ToArray();
        if (nuspecEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' must contain exactly one root nuspec; found {nuspecEntries.Length}.");
        }

        using var nuspecStream = nuspecEntries[0].Open();
        using var reader = XmlReader.Create(nuspecStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(reader);
        var metadata = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata");
        if (metadata is null)
        {
            throw new InvalidDataException($"'{Path.GetFileName(path)}' has no nuspec metadata element.");
        }

        var packageId = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value.Trim();
        var packageVersion = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "version")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(packageId) || packageVersion != expectedVersion)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' declares id={packageId ?? "(missing)"}, version={packageVersion ?? "(missing)"}; " +
                $"expected version {expectedVersion}.");
        }

        packageStream.Position = 0;
        var packageHash = Convert.ToBase64String(SHA512.HashData(packageStream));
        return new NuGetPackage(path, packageId, packageVersion, packageHash);
    }

    private static async Task<PackageResult> CheckExistingPackageAsync(NuGetPackage package)
    {
        var normalizedId = Uri.EscapeDataString(package.Id.ToLowerInvariant());
        var normalizedVersion = Uri.EscapeDataString(package.Version.ToLowerInvariant());
        var fileName = $"{normalizedId}.{normalizedVersion}.nupkg.sha512";
        var uri = new Uri($"{NuGetFlatContainer}/{normalizedId}/{normalizedVersion}/{fileName}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await HttpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PackageResult(package.Path, PackageState.New);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"NuGet returned HTTP {(int)response.StatusCode} while checking {package.Id} {package.Version}.");
        }

        var existingHash = (await response.Content.ReadAsStringAsync()).Trim();
        if (!string.Equals(existingHash, package.Sha512, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"NuGet already contains {package.Id} {package.Version} with different SHA-512 content. " +
                "The existing version cannot be replaced; select a new version or recover the original release run.");
        }
        return new PackageResult(package.Path, PackageState.Identical);
    }

    private static void WriteOutputs(int packageCount, int newPackageCount, int identicalPackageCount)
    {
        var outputPath = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidDataException("GITHUB_OUTPUT is not available; validation must run from a GitHub Action.");
        }

        var output = new StringBuilder()
            .AppendLine($"package-count={packageCount}")
            .AppendLine($"new-package-count={newPackageCount}")
            .AppendLine($"identical-package-count={identicalPackageCount}")
            .ToString();
        File.AppendAllText(outputPath, output, new UTF8Encoding(false));

        var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var summary = new StringBuilder()
                .AppendLine("## Validate NuGet Release Packages from Codebelt")
                .AppendLine()
                .AppendLine($"- Packages validated: {packageCount}")
                .AppendLine($"- New package versions: {newPackageCount}")
                .AppendLine($"- Identical existing package versions: {identicalPackageCount}")
                .AppendLine()
                .ToString();
            File.AppendAllText(summaryPath, summary, new UTF8Encoding(false));
        }
    }

    private sealed record NuGetPackage(string Path, string Id, string Version, string Sha512);

    private sealed record PackageResult(string Path, PackageState State);

    private enum PackageState
    {
        New,
        Identical
    }
}
