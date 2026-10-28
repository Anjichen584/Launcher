using System.Diagnostics;
using System.Text.Json;

namespace Launcher.Core;

/// <summary>
/// 安装 Fabric / Quilt（profile JSON）与 Forge / NeoForge（运行官方安装器 --installClient）。
/// </summary>
public sealed class LoaderInstaller
{
    private readonly VersionService _versions;
    private readonly JavaManager _java;

    public LoaderInstaller(VersionService versions, JavaManager java)
    {
        _versions = versions;
        _java = java;
    }

    public async Task<InstalledVersion> InstallAsync(
        LoaderKind kind,
        string mcVersion,
        LoaderBuild build,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await _versions.EnsureVanillaDescriptorAsync(mcVersion, ct);

        return kind switch
        {
            LoaderKind.Fabric or LoaderKind.Quilt => await InstallProfileAsync(kind, mcVersion, build, progress, ct),
            LoaderKind.Forge or LoaderKind.NeoForge => await InstallWithInstallerAsync(kind, mcVersion, build, progress, ct),
            _ => throw new ArgumentException("原版请使用 VersionService.InstallVanillaAsync", nameof(kind)),
        };
    }

    private async Task<InstalledVersion> InstallProfileAsync(
        LoaderKind kind, string mcVersion, LoaderBuild build, IProgress<string>? progress, CancellationToken ct)
    {
        var meta = kind == LoaderKind.Fabric ? LoaderCatalog.FabricMeta : LoaderCatalog.QuiltMeta;
        progress?.Report($"获取 {kind} profile");
        var json = await Downloader.GetStringAsync(new[] { $"{meta}/versions/loader/{mcVersion}/{build.Version}/profile/json" }, ct);

        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetString()
                 ?? throw new InvalidDataException("profile 缺少 id");

        var path = VersionResolver.JsonPathFor(_versions.VersionsDirectory, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, ct);

        return await _versions.PrepareAsync(id, progress, ct);
    }

    private async Task<InstalledVersion> InstallWithInstallerAsync(
        LoaderKind kind, string mcVersion, LoaderBuild build, IProgress<string>? progress, CancellationToken ct)
    {
        // 安装器需要 Java，且要求与原版相同的 Java 主版本
        var vanilla = VersionResolver.Resolve(_versions.VersionsDirectory, mcVersion);
        var javaExe = await _java.EnsureAsync(vanilla.Json.RequiredJavaMajor, progress, ct);

        var (installerUrls, fileName) = InstallerLocation(kind, mcVersion, build);
        var cacheDir = Path.Combine(_versions.GameRoot, "loader-cache");
        var installerPath = Path.Combine(cacheDir, fileName);

        progress?.Report($"下载 {kind} 安装器");
        await Downloader.DownloadAsync(installerUrls, installerPath, null, ct);

        // 安装器要求目录中存在 launcher_profiles.json
        var profiles = Path.Combine(_versions.GameRoot, "launcher_profiles.json");
        if (!File.Exists(profiles))
        {
            Directory.CreateDirectory(_versions.GameRoot);
            await File.WriteAllTextAsync(profiles, "{\"profiles\":{},\"version\":3}", ct);
        }

        var before = Directory.Exists(_versions.VersionsDirectory)
            ? Directory.GetDirectories(_versions.VersionsDirectory).Select(d => Path.GetFileName(d)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Report($"运行 {kind} 安装器（可能需要数分钟）");
        await RunInstallerAsync(javaExe, installerPath, _versions.GameRoot, progress, ct);

        var id = FindInstalledLoaderId(kind, mcVersion, before)
                 ?? throw new InvalidOperationException($"安装器结束，但没有找到 {kind} 的版本描述");

        return await _versions.PrepareAsync(id, progress, ct);
    }

    private static (string[] Urls, string FileName) InstallerLocation(LoaderKind kind, string mcVersion, LoaderBuild build)
    {
        if (kind == LoaderKind.Forge)
        {
            var raw = build.RawVersion ?? $"{mcVersion}-{build.Version}";
            var rel = $"net/minecraftforge/forge/{raw}/forge-{raw}-installer.jar";
            return (new[]
            {
                $"{MirrorSource.BmclApi}/maven/{rel}",
                $"https://maven.minecraftforge.net/{rel}",
            }, $"forge-{raw}-installer.jar");
        }

        // NeoForge
        if (!string.IsNullOrEmpty(build.InstallerPath))
        {
            var path = build.InstallerPath.TrimStart('/');            // maven/net/neoforged/...
            var rel = path.StartsWith("maven/", StringComparison.Ordinal) ? path["maven/".Length..] : path;
            return (new[]
            {
                $"{MirrorSource.BmclApi}/{path}",
                $"https://maven.neoforged.net/releases/{rel}",
            }, Path.GetFileName(rel));
        }

        // 旧版 NeoForge（如 1.20.1）使用 net.neoforged:forge 坐标
        var raw2 = build.RawVersion ?? $"{mcVersion}-{build.Version}";
        var rel2 = $"net/neoforged/forge/{raw2}/forge-{raw2}-installer.jar";
        return (new[]
        {
            $"{MirrorSource.BmclApi}/maven/{rel2}",
            $"https://maven.neoforged.net/releases/{rel2}",
        }, $"forge-{raw2}-installer.jar");
    }

    private string? FindInstalledLoaderId(LoaderKind kind, string mcVersion, HashSet<string> before)
    {
        // 两者的版本目录名都包含 "forge"（neoforge 也含此子串）
        const string keyword = "forge";
        var candidates = Directory.GetDirectories(_versions.VersionsDirectory)
            .Select(d => Path.GetFileName(d))
            .Where(n => File.Exists(VersionResolver.JsonPathFor(_versions.VersionsDirectory, n)))
            .Where(n =>
            {
                try
                {
                    var raw = VersionResolver.LoadRaw(_versions.VersionsDirectory, n);
                    return raw.InheritsFrom == mcVersion
                           && (n.Contains(keyword, StringComparison.OrdinalIgnoreCase) || !before.Contains(n));
                }
                catch
                {
                    return false;
                }
            })
            .ToList();

        // 新出现的目录优先；否则取最近修改的
        return candidates
            .OrderByDescending(n => !before.Contains(n))
            .ThenByDescending(n => File.GetLastWriteTimeUtc(VersionResolver.JsonPathFor(_versions.VersionsDirectory, n)))
            .FirstOrDefault();
    }

    private static async Task RunInstallerAsync(
        string javaExe, string installerJar, string gameRoot, IProgress<string>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = gameRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJar);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(gameRoot);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动安装器进程");
        using var killOnCancel = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
        });

        var stdout = PumpAsync(process.StandardOutput, progress);
        var stderr = PumpAsync(process.StandardError, progress);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdout, stderr);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"安装器失败，退出码 {process.ExitCode}");
        }
    }

    private static async Task PumpAsync(StreamReader reader, IProgress<string>? progress)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Trim().Length > 0) progress?.Report("[安装器] " + line);
        }
    }
}
