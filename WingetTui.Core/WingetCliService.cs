using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WingetTui.Core;

public partial class WingetCliService : IPackageService
{
    private sealed record WingetResult(
        int ExitCode,
        string Output,
        string Error);
    
    public IReadOnlyList<PackageInfo> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var result = RunWinget(
            "search", query, 
            "--disable-interactivity", 
            "--accept-source-agreements");
        
        const int noApplicationsFound = unchecked((int)0x8A150014);

        const string noMatchMessage =
            "No package found matching input criteria.";

        if (result.ExitCode == noApplicationsFound ||
            result.Output.Contains(
                noMatchMessage,
                StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains(
                noMatchMessage,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (result.ExitCode == 0) return ParseTable(result.Output);
        var message = string.IsNullOrWhiteSpace(result.Error)
            ? $"winget exited with code {result.ExitCode}."
            : result.Error.Trim();

        throw new InvalidOperationException(message);

    }

    public void Install(PackageInfo package, bool acceptPackageAgreements)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (string.IsNullOrWhiteSpace(package.Id)) 
            throw new ArgumentException("Package ID cannot be empty", nameof(package));

        var args = new List<string>
        {
            "install", 
            "--id", package.Id, 
            "--exact", 
            "--accept-source-agreements", 
            "--disable-interactivity"
        };

        if (acceptPackageAgreements)
        {
            args.Add("--accept-package-agreements");
        }
        
        var result = RunWinget(args.ToArray());

        if (result.ExitCode == 0)
        {
            return;
        }

        var message = !string.IsNullOrWhiteSpace(result.Error)
            ? result.Error.Trim()
            : !string.IsNullOrWhiteSpace(result.Output)
                ? result.Output.Trim()
                : $"winget exited with code 0x{result.ExitCode:x8}";
        throw new InvalidOperationException(
            $"Failed to install {package.Id}: {message}");
    }

    public void Upgrade(PackageInfo package, bool acceptPackageAgreements)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (string.IsNullOrWhiteSpace(package.Id)) 
            throw new ArgumentException("Package ID cannot be empty", nameof(package));
        
        var args = new List<string>
        {
            "upgrade",
            "--id", package.Id,
            "--exact",
            "--accept-source-agreements",
            "--disable-interactivity"
        };

        if (acceptPackageAgreements)
        {
            args.Add("--accept-package-agreements");
        }

        var result = RunWinget(args.ToArray());

        if (result.ExitCode == 0)
        {
            return;
        }

        const int noApplicableUpdate = unchecked((int)0x8A15002B);

        if (result.ExitCode == noApplicableUpdate)
        {
            throw new InvalidOperationException(
                $"No applicable update found for {package.Id}.");
        }

        var message = !string.IsNullOrWhiteSpace(result.Error)
            ? result.Error.Trim()
            : !string.IsNullOrWhiteSpace(result.Output)
                ? result.Output.Trim()
                : $"winget exited with code 0x{result.ExitCode:X8}.";

        throw new InvalidOperationException(
            $"Failed to upgrade {package.Id}: {message}");
    }

    private static WingetResult RunWinget(params string[] args)
    {
        var psi = new ProcessStartInfo("winget")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start winget");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        return new WingetResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult(),
            errorTask.GetAwaiter().GetResult());
    }

    private static IReadOnlyList<PackageInfo> ParseTable(string output)
    {
        var lines = output.Split('\n').Select(l =>
        {
            l = l.TrimEnd('\r');
            var i = l.LastIndexOf('\r');
            return i >= 0 ? l[(i + 1)..] : l;
        }).ToArray();

        var sep = Array.FindIndex(lines, l => l.Length > 10 && l.All(c => c == '-'));
        if (sep < 1)
        {
            throw new FormatException(
                "Could not parse winget output: table separator not found.");
        }

        var headers = MyRegex().Split(lines[sep - 1].Trim());
        var mergedVersionSource = Array.FindIndex(
            headers,
            h => h.Equals(
                "Version Source",
                StringComparison.OrdinalIgnoreCase));

        var hasCompactVersionSourceHeader = mergedVersionSource >= 0;

        if (hasCompactVersionSourceHeader)
        {
            headers =
            [
                .. headers
                    .Take(mergedVersionSource),

                "Version", "Source",
                .. headers.Skip(mergedVersionSource + 1)
            ];
        }
        int nameCol = Col("Name"), idCol = Col("Id"), verCol = Col("Version"),
            matchCol = Col("Match"), srcCol = Col("Source");
        if (nameCol < 0 || idCol < 0)
        {
            throw new FormatException(
                "Could not parse winget output: Name or Id column not found.");
        }

        var result = new List<PackageInfo>();
        foreach (var line in lines.Skip(sep + 1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (hasCompactVersionSourceHeader)
            {
                var sourceMatch = Regex.Match(
                    line.Trim(),
                    @"^(?<left>.+?)\s{2,}(?<source>\S+)\s*$");

                if (!sourceMatch.Success) continue;

                var packageMatch = Regex.Match(
                    sourceMatch.Groups["left"].Value,
                    @"^(?<name>.+)\s+(?<id>\S+)\s+(?<version>\S+)$");

                if (!packageMatch.Success) continue;

                result.Add(new PackageInfo(
                    Id: packageMatch.Groups["id"].Value,
                    Name: packageMatch.Groups["name"].Value,
                    Version: packageMatch.Groups["version"].Value,
                    Source: sourceMatch.Groups["source"].Value));

                continue;
            }
            
            var cells = MyRegex1().Split(line.Trim()).ToList();

            if (matchCol >= 0 && cells.Count == headers.Length - 1)
                cells.Insert(matchCol, "");

            if (cells.Count != headers.Length) continue; // footer/notice lines

            result.Add(new PackageInfo(
                Id: cells[idCol],
                Name: cells[nameCol],
                Version: verCol >= 0 ? cells[verCol] : "",
                Match: matchCol >= 0 ? cells[matchCol] : "",
                Source: srcCol >= 0 ? cells[srcCol] : ""));
        }
        return result;
        int Col(string name) => Array.FindIndex(headers, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MyRegex1();
}