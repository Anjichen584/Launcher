using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Core;

/// <summary>安装完成后，启动游戏所需的全部路径。</summary>
public sealed record InstalledVersion(
    VersionJson Json,
    string ClientJar,
    IReadOnlyList<string> ClasspathEntries,
    string NativesDirectory,
    string AssetsDirectory,
    string LibrariesDirectory);

/// <summary>
/// 负责获取版本清单并把某个版本安装到 .minecraft 目录中（镜像优先，失败回退官方源）。
/// </summary>
public sealed class VersionService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string OfficialAssetBase = "https://resources.download.minecraft.net";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _gameRoot;

    public VersionService(string gameRoot)
    {
        _gameRoot = gameRoot;
    }

    public string VersionsDirectory => Path.Combine(_gameRoot, "versions");
    public string LibrariesDirectory => Path.Combine(_gameRoot, "libraries");
    public string AssetsDirectory => Path.Combine(_gameRoot, "assets");

    public async Task<VersionManifest> GetManifestAsync(CancellationToken ct = default)
    {
        var json = await Downloader.GetStringAsync(MirrorSource.Candidates(ManifestUrl), ct);
        return JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions)
               ?? throw new InvalidDataException("版本清单解析失败");
    }

    public async Task<InstalledVersion> InstallAsync(
        ManifestVersion entry,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var versionDir = Path.Combine(VersionsDirectory, entry.Id);
        var jsonPath = Path.Combine(versionDir, entry.Id + ".json");

        progress?.Report($"下载版本描述 {entry.Id}");
        await Downloader.DownloadAsync(MirrorSource.Candidates(entry.Url), jsonPath,
            string.IsNullOrEmpty(entry.Sha1) ? null : entry.Sha1, ct);

        var version = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(jsonPath, ct), JsonOptions)
                      ?? throw new InvalidDataException("版本 JSON 解析失败");

        // 客户端 jar
        var clientJar = Path.Combine(versionDir, entry.Id + ".jar");
        if (version.Downloads?.TryGetValue("client", out var client) == true)
        {
            progress?.Report("下载游戏本体");
            await Downloader.DownloadAsync(MirrorSource.Candidates(client.Url), clientJar, client.Sha1, ct);
        }

        // 依赖库与 natives
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);
        var classpath = new List<string>();

        foreach (var lib in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;

            var artifact = lib.Downloads?.Artifact;
            if (artifact?.Path is not null)
            {
                var dest = Path.Combine(LibrariesDirectory, artifact.Path);
                progress?.Report($"下载依赖 {lib.Name}");
                await Downloader.DownloadAsync(MirrorSource.Candidates(artifact.Url), dest, artifact.Sha1, ct);
                classpath.Add(dest);

                // 新版本：natives 作为独立库发布，名称包含 natives-windows
                if (lib.Name.Contains(":natives-windows", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractNatives(dest, nativesDir);
                }
            }

            // 旧版本：natives 通过 classifier 下载
            if (lib.Natives is not null
                && lib.Natives.TryGetValue(RuleEvaluator.TargetOs, out var classifierTemplate))
            {
                var classifier = classifierTemplate.Replace("${arch}", "64");
                if (lib.Downloads?.Classifiers?.TryGetValue(classifier, out var nativeArtifact) == true
                    && nativeArtifact.Path is not null)
                {
                    var nativeDest = Path.Combine(LibrariesDirectory, nativeArtifact.Path);
                    await Downloader.DownloadAsync(MirrorSource.Candidates(nativeArtifact.Url),
                        nativeDest, nativeArtifact.Sha1, ct);
                    ExtractNatives(nativeDest, nativesDir);
                }
            }
        }

        // 资源索引与资源文件
        if (version.AssetIndex is { } indexRef)
        {
            await InstallAssetsAsync(indexRef, progress, ct);
        }

        return new InstalledVersion(
            version,
            clientJar,
            classpath,
            nativesDir,
            AssetsDirectory,
            LibrariesDirectory);
    }

    private async Task InstallAssetsAsync(AssetIndexRef indexRef, IProgress<string>? progress, CancellationToken ct)
    {
        var indexPath = Path.Combine(AssetsDirectory, "indexes", indexRef.Id + ".json");
        progress?.Report("下载资源索引");
        await Downloader.DownloadAsync(MirrorSource.Candidates(indexRef.Url), indexPath, indexRef.Sha1, ct);

        var index = JsonSerializer.Deserialize<AssetIndex>(await File.ReadAllTextAsync(indexPath, ct), JsonOptions)
                    ?? throw new InvalidDataException("资源索引解析失败");

        var objects = index.Objects.Values.Select(o => o.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var done = 0;
        await Parallel.ForEachAsync(objects,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (hash, token) =>
            {
                var prefix = hash[..2];
                var dest = Path.Combine(AssetsDirectory, "objects", prefix, hash);
                var urls = MirrorSource.Candidates($"{OfficialAssetBase}/{prefix}/{hash}");
                await Downloader.DownloadAsync(urls, dest, hash, token);

                var current = Interlocked.Increment(ref done);
                if (current % 200 == 0 || current == objects.Count)
                {
                    progress?.Report($"下载资源文件 {current}/{objects.Count}");
                }
            });
    }

    private static void ExtractNatives(string jarPath, string nativesDir)
    {
        using var zip = ZipFile.OpenRead(jarPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!IsNativeBinary(entry.Name)) continue;

            var target = Path.Combine(nativesDir, entry.Name);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool IsNativeBinary(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase);

    public sealed class AssetIndex
    {
        public Dictionary<string, AssetObject> Objects { get; set; } = new();
    }

    public sealed class AssetObject
    {
        public string Hash { get; set; } = "";
        public long Size { get; set; }
    }
}
