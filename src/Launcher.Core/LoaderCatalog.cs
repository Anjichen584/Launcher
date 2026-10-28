using System.Text.Json;

namespace Launcher.Core;

public enum LoaderKind
{
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge,
}

/// <summary>一个可安装的加载器版本。</summary>
/// <param name="Version">展示与安装用的版本号（如 0.16.9 / 47.1.5）。</param>
/// <param name="RawVersion">Forge 旧格式的完整版本（如 1.20.1-47.1.5），用于构造安装器路径。</param>
/// <param name="InstallerPath">NeoForge 提供的安装器路径（如 /maven/net/neoforged/...）。</param>
public sealed record LoaderBuild(string Version, string? RawVersion = null, string? InstallerPath = null);

/// <summary>查询某个 Minecraft 版本可用的加载器版本（镜像优先）。</summary>
public static class LoaderCatalog
{
    public const string FabricMeta = "https://meta.fabricmc.net/v2";
    public const string QuiltMeta = "https://meta.quiltmc.org/v3";

    public static async Task<IReadOnlyList<LoaderBuild>> GetBuildsAsync(LoaderKind kind, string mcVersion, CancellationToken ct = default)
    {
        switch (kind)
        {
            case LoaderKind.Vanilla:
                return Array.Empty<LoaderBuild>();

            case LoaderKind.Fabric:
            case LoaderKind.Quilt:
            {
                var meta = kind == LoaderKind.Fabric ? FabricMeta : QuiltMeta;
                var json = await Downloader.GetStringAsync(new[] { $"{meta}/versions/loader/{mcVersion}" }, ct);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.GetProperty("loader").GetProperty("version").GetString() ?? "")
                    .Where(v => v.Length > 0)
                    .Select(v => new LoaderBuild(v))
                    .ToList();
            }

            case LoaderKind.Forge:
            {
                var json = await Downloader.GetStringAsync(MirrorSource.Candidates(
                    $"{MirrorSource.BmclApi}/forge/minecraft/{mcVersion}"), ct);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.GetProperty("version").GetString() ?? "")
                    .Where(v => v.Length > 0)
                    .OrderByDescending(v => v, VersionComparer.Instance)
                    .Select(v => new LoaderBuild(v, RawVersion: $"{mcVersion}-{v}"))
                    .ToList();
            }

            case LoaderKind.NeoForge:
            {
                var json = await Downloader.GetStringAsync(MirrorSource.Candidates(
                    $"{MirrorSource.BmclApi}/neoforge/list/{mcVersion}"), ct);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.EnumerateArray()
                    .Select(e => new LoaderBuild(
                        e.GetProperty("version").GetString() ?? "",
                        e.TryGetProperty("rawVersion", out var raw) ? raw.GetString() : null,
                        e.TryGetProperty("installerPath", out var ip) ? ip.GetString() : null))
                    .Where(b => b.Version.Length > 0)
                    .OrderByDescending(b => b.Version, VersionComparer.Instance)
                    .ToList();
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>按点分数字比较版本号（47.1.5 > 47.1.4 > 47.0.10）。</summary>
    internal sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").Split('.', '-');
            var b = (y ?? "").Split('.', '-');
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var pa = i < a.Length && int.TryParse(a[i], out var na) ? na : 0;
                var pb = i < b.Length && int.TryParse(b[i], out var nb) ? nb : 0;
                if (pa != pb) return pa.CompareTo(pb);
            }
            return string.CompareOrdinal(x, y);
        }
    }
}
