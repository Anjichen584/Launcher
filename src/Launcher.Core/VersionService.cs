using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Core;

/// <summary>准备完成后，启动游戏所需的全部信息。</summary>
public sealed record InstalledVersion(
    VersionJson Json,
    string ClientJar,
    IReadOnlyList<string> ClasspathEntries,
    string NativesDirectory,
    string AssetsDirectory,
    string LibrariesDirectory);

/// <summary>
/// 版本与文件管理：获取清单、安装原版、按继承链准备某个版本（客户端、库、natives、资源）。
/// 所有下载都是镜像优先、官方回退，并做 SHA1 校验（有提供时）。
/// </summary>
public sealed class VersionService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string OfficialLibraryBase = "https://libraries.minecraft.net/";
    public const string OfficialAssetBase = "https://resources.download.minecraft.net";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _gameRoot;

    public VersionService(string gameRoot)
    {
        _gameRoot = gameRoot;
    }

    public string GameRoot => _gameRoot;
    public string VersionsDirectory => Path.Combine(_gameRoot, "versions");
    public string LibrariesDirectory => Path.Combine(_gameRoot, "libraries");
    public string AssetsDirectory => Path.Combine(_gameRoot, "assets");

    public async Task<VersionManifest> GetManifestAsync(CancellationToken ct = default)
    {
        var json = await Downloader.GetStringAsync(MirrorSource.Candidates(ManifestUrl), ct);
        return JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions)
               ?? throw new InvalidDataException("版本清单解析失败");
    }

    /// <summary>确保原版版本描述存在（不存在则从清单下载）。不会下载客户端和资源。</summary>
    public async Task EnsureVanillaDescriptorAsync(string mcVersion, CancellationToken ct = default)
    {
        if (File.Exists(VersionResolver.JsonPathFor(VersionsDirectory, mcVersion))) return;

        var manifest = await GetManifestAsync(ct);
        var entry = manifest.Versions.FirstOrDefault(v => v.Id == mcVersion)
                    ?? throw new InvalidOperationException($"版本清单中找不到 {mcVersion}");

        await Downloader.DownloadAsync(MirrorSource.Candidates(entry.Url),
            VersionResolver.JsonPathFor(VersionsDirectory, entry.Id),
            string.IsNullOrEmpty(entry.Sha1) ? null : entry.Sha1, ct);
    }

    /// <summary>安装原版：描述 + 全部文件。</summary>
    public async Task<InstalledVersion> InstallVanillaAsync(string mcVersion, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report($"准备原版 {mcVersion}");
        await EnsureVanillaDescriptorAsync(mcVersion, ct);
        return await PrepareAsync(mcVersion, progress, ct);
    }

    /// <summary>
    /// 按继承链准备某个已存在描述的版本：下载客户端 jar、依赖库、解压 natives、下载资源。
    /// 对已存在且校验通过的文件会跳过，因此可重复调用。
    /// </summary>
    public async Task<InstalledVersion> PrepareAsync(string versionId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var resolved = VersionResolver.Resolve(VersionsDirectory, versionId);
        var v = resolved.Json;

        // 客户端 jar（继承版本使用父版本的 jar）
        var jarPath = Path.Combine(VersionsDirectory, resolved.JarId, resolved.JarId + ".jar");
        if (v.Downloads?.TryGetValue("client", out var client) == true)
        {
            progress?.Report("校验游戏本体");
            await Downloader.DownloadAsync(MirrorSource.Candidates(client.Url), jarPath, client.Sha1, ct);
        }
        else if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException("缺少客户端 jar，且版本描述中没有下载地址", jarPath);
        }

        // 依赖库与 natives
        var nativesDir = Path.Combine(VersionsDirectory, versionId, "natives");
        Directory.CreateDirectory(nativesDir);
        var classpath = new List<string>();

        foreach (var lib in v.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;

            var (relPath, url, sha1) = ResolveArtifact(lib);
            if (relPath is null) continue;

            var dest = Path.Combine(LibrariesDirectory, relPath);
            progress?.Report($"校验依赖 {lib.Name}");
            await Downloader.DownloadAsync(MirrorSource.Candidates(url!), dest, sha1, ct);
            classpath.Add(dest);

            // 新版 natives：作为独立库发布，名称形如 ...:natives-windows / natives-linux / natives-macos-arm64
            if (IsNativesLibraryForThisPlatform(lib.Name))
            {
                ExtractNatives(dest, nativesDir);
            }

            // 旧版 natives：通过 classifier 下载
            if (lib.Natives is not null && lib.Natives.TryGetValue(Platform.OsName, out var classifierTemplate))
            {
                var classifier = classifierTemplate.Replace("${arch}", Platform.ArchBits);
                if (lib.Downloads?.Classifiers?.TryGetValue(classifier, out var nativeArtifact) == true
                    && nativeArtifact.Path is not null)
                {
                    var nativeDest = Path.Combine(LibrariesDirectory, nativeArtifact.Path);
                    await Downloader.DownloadAsync(MirrorSource.Candidates(ArtifactUrl(nativeArtifact)),
                        nativeDest, nativeArtifact.Sha1, ct);
                    ExtractNatives(nativeDest, nativesDir);
                }
            }
        }

        if (v.AssetIndex is { } indexRef)
        {
            await InstallAssetsAsync(indexRef, progress, ct);
        }

        return new InstalledVersion(
            v,
            jarPath,
            classpath.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            nativesDir,
            AssetsDirectory,
            LibrariesDirectory);
    }

    /// <summary>
    /// 解析库的下载信息：优先使用 downloads.artifact；否则根据 Maven 坐标与 url 字段拼接（加载器库）。
    /// 返回 (相对路径, 下载地址, sha1)；路径为 null 表示该库没有可下载的主文件。
    /// </summary>
    public static (string? RelPath, string? Url, string? Sha1) ResolveArtifact(Library lib)
    {
        var artifact = lib.Downloads?.Artifact;
        if (artifact?.Path is not null)
        {
            return (artifact.Path, ArtifactUrl(artifact), artifact.Sha1);
        }

        if (string.IsNullOrEmpty(lib.Name)) return (null, null, null);

        var rel = MavenCoordinates.ToRelativePath(lib.Name);
        var baseUrl = string.IsNullOrEmpty(lib.Url) ? OfficialLibraryBase : lib.Url;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return (rel, baseUrl + rel, lib.Sha1);
    }

    private static string ArtifactUrl(Artifact artifact) =>
        string.IsNullOrEmpty(artifact.Url) ? OfficialLibraryBase + artifact.Path : artifact.Url;

    /// <summary>判断库是否为当前平台的 natives（新版命名规则）。</summary>
    public static bool IsNativesLibraryForThisPlatform(string name)
    {
        var idx = name.IndexOf(":natives-", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var classifier = name[(idx + ":natives-".Length)..];
        var isArm = classifier.EndsWith("arm64", StringComparison.OrdinalIgnoreCase)
                    || classifier.EndsWith("aarch64", StringComparison.OrdinalIgnoreCase);
        var wantArm = Platform.Arch == "aarch64";

        var osMatches = Platform.OsName switch
        {
            "windows" => classifier.StartsWith("windows", StringComparison.OrdinalIgnoreCase),
            "osx" => classifier.StartsWith("macos", StringComparison.OrdinalIgnoreCase)
                     || classifier.StartsWith("osx", StringComparison.OrdinalIgnoreCase),
            _ => classifier.StartsWith("linux", StringComparison.OrdinalIgnoreCase),
        };

        return osMatches && isArm == wantArm;
    }

    private async Task InstallAssetsAsync(AssetIndexRef indexRef, IProgress<string>? progress, CancellationToken ct)
    {
        var indexPath = Path.Combine(AssetsDirectory, "indexes", indexRef.Id + ".json");
        progress?.Report("校验资源索引");
        await Downloader.DownloadAsync(MirrorSource.Candidates(indexRef.Url), indexPath,
            string.IsNullOrEmpty(indexRef.Sha1) ? null : indexRef.Sha1, ct);

        var index = JsonSerializer.Deserialize<AssetIndex>(await File.ReadAllTextAsync(indexPath, ct), JsonOptions)
                    ?? throw new InvalidDataException("资源索引解析失败");

        var hashes = index.Objects.Values.Select(o => o.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var done = 0;
        await Parallel.ForEachAsync(hashes,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (hash, token) =>
            {
                var prefix = hash[..2];
                var dest = Path.Combine(AssetsDirectory, "objects", prefix, hash);
                var urls = MirrorSource.Candidates($"{OfficialAssetBase}/{prefix}/{hash}");
                await Downloader.DownloadAsync(urls, dest, hash, token);

                var current = Interlocked.Increment(ref done);
                if (current % 200 == 0 || current == hashes.Count)
                {
                    progress?.Report($"下载资源文件 {current}/{hashes.Count}");
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
            if (!Platform.IsNativeBinary(entry.Name)) continue;

            entry.ExtractToFile(Path.Combine(nativesDir, entry.Name), overwrite: true);
        }
    }

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
