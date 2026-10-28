using System.Text.Json;

namespace Launcher.Core;

/// <summary>继承链解析完成后的版本：合并后的描述 + 实际使用的客户端 jar 所属版本。</summary>
public sealed record ResolvedVersion(VersionJson Json, string JarId);

/// <summary>
/// 解析 inheritsFrom 继承链（如 Fabric/Forge 依赖原版）。
/// 合并规则：子版本覆盖标量字段；库与参数列表为"子 + 父"。
/// </summary>
public static class VersionResolver
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private const int MaxDepth = 8;

    public static string JsonPathFor(string versionsDir, string id) =>
        Path.Combine(versionsDir, id, id + ".json");

    public static VersionJson LoadRaw(string versionsDir, string id)
    {
        var path = JsonPathFor(versionsDir, id);
        if (!File.Exists(path)) throw new FileNotFoundException($"版本描述文件不存在: {id}", path);
        return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path), Options)
               ?? throw new InvalidDataException($"版本 JSON 解析失败: {id}");
    }

    public static ResolvedVersion Resolve(string versionsDir, string id) => Resolve(versionsDir, id, 0);

    private static ResolvedVersion Resolve(string versionsDir, string id, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException($"版本继承层级过深或存在循环: {id}");

        var raw = LoadRaw(versionsDir, id);
        if (string.IsNullOrEmpty(raw.InheritsFrom))
        {
            return new ResolvedVersion(raw, raw.Jar ?? raw.Id);
        }

        var parent = Resolve(versionsDir, raw.InheritsFrom, depth + 1);
        var p = parent.Json;

        var merged = new VersionJson
        {
            Id = raw.Id,
            MainClass = string.IsNullOrEmpty(raw.MainClass) ? p.MainClass : raw.MainClass,
            MinecraftArguments = (raw.Arguments is null && raw.MinecraftArguments is null)
                ? p.MinecraftArguments
                : raw.MinecraftArguments,
            Arguments = MergeArguments(p.Arguments, raw.Arguments),
            AssetIndex = raw.AssetIndex ?? p.AssetIndex,
            Assets = raw.Assets ?? p.Assets,
            Downloads = raw.Downloads ?? p.Downloads,
            JavaVersion = raw.JavaVersion ?? p.JavaVersion,
            Jar = raw.Jar ?? parent.JarId,
            Libraries = raw.Libraries.Concat(p.Libraries).ToList(),
        };

        return new ResolvedVersion(merged, merged.Jar!);
    }

    private static ArgumentsNode? MergeArguments(ArgumentsNode? parent, ArgumentsNode? child)
    {
        if (parent is null && child is null) return null;
        return new ArgumentsNode
        {
            Game = (parent?.Game ?? new List<JsonElement>()).Concat(child?.Game ?? new List<JsonElement>()).ToList(),
            Jvm = (parent?.Jvm ?? new List<JsonElement>()).Concat(child?.Jvm ?? new List<JsonElement>()).ToList(),
        };
    }
}
