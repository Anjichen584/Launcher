using System.Formats.Tar;
using System.IO.Compression;

namespace Launcher.Core;

/// <summary>
/// 托管的 Java 运行时：按需从 Adoptium 下载指定主版本的 JDK 到 runtime/ 目录。
/// </summary>
public sealed class JavaManager
{
    private readonly string _runtimeRoot;

    public JavaManager(string gameRoot)
    {
        _runtimeRoot = Path.Combine(gameRoot, "runtime");
    }

    public string RuntimeRoot => _runtimeRoot;

    /// <summary>返回指定主版本的 java 可执行文件路径；本地没有则自动下载。</summary>
    public async Task<string> EnsureAsync(int majorVersion, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var installDir = Path.Combine(_runtimeRoot, $"java-{majorVersion}");
        var existing = FindJavaExecutable(installDir);
        if (existing is not null) return existing;

        Directory.CreateDirectory(_runtimeRoot);
        var isWindows = Platform.IsWindows;
        var ext = isWindows ? "zip" : "tar.gz";
        var archive = Path.Combine(_runtimeRoot, $"jdk-{majorVersion}.{ext}");

        var url = "https://api.adoptium.net/v3/binary/latest/" + majorVersion
                  + "/ga/" + Platform.AdoptiumOs + "/" + Platform.AdoptiumArch
                  + "/jdk/hotspot/normal/eclipse?project=jdk";

        progress?.Report($"下载 Java {majorVersion}（约 100–200 MB，仅需一次）");
        await Downloader.DownloadAsync(new[] { url }, archive, null, ct);

        progress?.Report($"解压 Java {majorVersion}");
        var staging = installDir + ".tmp";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        if (isWindows)
        {
            ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);
        }
        else
        {
            await using var fs = File.OpenRead(archive);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, staging, overwriteFiles: true, ct);
        }

        if (Directory.Exists(installDir)) Directory.Delete(installDir, recursive: true);
        Directory.Move(staging, installDir);
        File.Delete(archive);

        var exe = FindJavaExecutable(installDir)
                  ?? throw new InvalidOperationException($"解压后找不到 Java {majorVersion} 的可执行文件");

        EnsureExecutable(exe);
        return exe;
    }

    /// <summary>在目录中查找 bin/java(.exe)（兼容 macOS 的 Contents/Home/bin 结构）。</summary>
    public static string? FindJavaExecutable(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (var file in Directory.EnumerateFiles(dir, Platform.JavaExecutableName, SearchOption.AllDirectories))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(file));
            if (string.Equals(parent, "bin", StringComparison.OrdinalIgnoreCase)) return file;
        }
        return null;
    }

    private static void EnsureExecutable(string exe)
    {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(exe);
        File.SetUnixFileMode(exe, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
