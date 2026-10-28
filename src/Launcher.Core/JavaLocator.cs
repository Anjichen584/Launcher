using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Launcher.Core;

public sealed record JavaInstall(string Path, int MajorVersion);

/// <summary>检测本机 Java 运行时（JAVA_HOME、PATH、常见安装目录）。</summary>
public static class JavaLocator
{
    private static readonly Regex VersionRegex = new(@"version ""(?<v>[^""]+)""", RegexOptions.Compiled);

    public static async Task<IReadOnlyList<JavaInstall>> FindAsync(CancellationToken ct = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(System.IO.Path.Combine(javaHome, "bin", Platform.JavaExecutableName));
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(System.IO.Path.Combine(dir, Platform.JavaExecutableName));
        }

        foreach (var root in CommonRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                candidates.Add(System.IO.Path.Combine(dir, "bin", Platform.JavaExecutableName));
                candidates.Add(System.IO.Path.Combine(dir, "Contents", "Home", "bin", Platform.JavaExecutableName));
            }
        }

        var results = new List<JavaInstall>();
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var major = await ProbeMajorVersionAsync(candidate, ct);
            if (major is int m && results.All(r => !string.Equals(r.Path, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new JavaInstall(candidate, m));
            }
        }

        return results.OrderByDescending(r => r.MajorVersion).ToList();
    }

    /// <summary>解析 `java -version` 输出，返回主版本号（Java 8 为 8，Java 17 为 17）。</summary>
    public static int? ParseMajorVersion(string versionOutput)
    {
        var match = VersionRegex.Match(versionOutput);
        if (!match.Success) return null;

        var parts = match.Groups["v"].Value.Split('.', '-', '_', '+');
        if (!int.TryParse(parts[0], out var first)) return null;
        if (first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second)) return second;
        return first;
    }

    private static async Task<int?> ProbeMajorVersionAsync(string javaExe, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return ParseMajorVersion(stderr + stdout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static IEnumerable<string> CommonRoots()
    {
        if (!Platform.IsWindows)
        {
            yield return "/usr/lib/jvm";
            yield return "/Library/Java/JavaVirtualMachines";
            yield return "/usr/local/lib/jvm";
            yield break;
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var baseDir in new[] { pf, pf86 }.Where(d => !string.IsNullOrEmpty(d)))
        {
            yield return System.IO.Path.Combine(baseDir, "Java");
            yield return System.IO.Path.Combine(baseDir, "Eclipse Adoptium");
            yield return System.IO.Path.Combine(baseDir, "Microsoft");
            yield return System.IO.Path.Combine(baseDir, "Zulu");
            yield return System.IO.Path.Combine(baseDir, "BellSoft");
        }
    }
}
