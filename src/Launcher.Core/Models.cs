using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestVersions Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<ManifestVersion> Versions { get; set; } = new();
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = "";
}

public sealed class ManifestVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = "";

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; set; }
}

/// <summary>单个版本的 JSON 描述（对应 version.json）。</summary>
public sealed class VersionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = "";

    /// <summary>旧版本使用的单字符串参数。</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    /// <summary>新版本（1.13+）使用的结构化参数。</summary>
    [JsonPropertyName("arguments")]
    public ArgumentsNode? Arguments { get; set; }

    [JsonPropertyName("assetIndex")]
    public AssetIndexRef? AssetIndex { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("downloads")]
    public Dictionary<string, Artifact>? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    /// <summary>继承的父版本（加载器版本依赖原版）。</summary>
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    /// <summary>实际使用的客户端 jar 所属版本 ID（缺省为自身）。</summary>
    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    /// <summary>该版本要求的 Java 主版本（缺省视为 Java 8）。</summary>
    [JsonPropertyName("javaVersion")]
    public JavaVersionRef? JavaVersion { get; set; }

    public int RequiredJavaMajor => JavaVersion?.MajorVersion ?? 8;
}

public sealed class JavaVersionRef
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = "";

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public sealed class ArgumentsNode
{
    [JsonPropertyName("game")]
    public List<JsonElement> Game { get; set; } = new();

    [JsonPropertyName("jvm")]
    public List<JsonElement> Jvm { get; set; } = new();
}

public sealed class AssetIndexRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>通用下载条目（client.jar、库文件、资源索引等）。</summary>
public class Artifact
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string Sha1 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class Library
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; set; }

    /// <summary>旧版本的 natives 映射，例如 {"windows": "natives-windows-${arch}"}。</summary>
    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    /// <summary>加载器库常用的仓库地址（无 downloads 字段时使用）。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public Artifact? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, Artifact>? Classifiers { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow";

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }
}
