using System.Text.Json;

namespace Launcher.Core;

public sealed class LaunchOptions
{
    public required string JavaPath { get; init; }
    public required string GameDirectory { get; init; }
    public required string AssetsDirectory { get; init; }
    public required string LibrariesDirectory { get; init; }
    public required string NativesDirectory { get; init; }
    public required string ClientJar { get; init; }
    public required IReadOnlyList<string> ClasspathEntries { get; init; }

    public required string Username { get; init; }
    public required string Uuid { get; init; }
    public string AccessToken { get; init; } = "0";
    public string UserType { get; init; } = "legacy";

    public int MinMemoryMb { get; init; } = 512;
    public int MaxMemoryMb { get; init; } = 2048;
    public IReadOnlyList<string> ExtraJvmArgs { get; init; } = Array.Empty<string>();

    public string LauncherName { get; init; } = "McLauncher";
    public string LauncherVersion { get; init; } = "0.1.0";
    public string ClasspathSeparator { get; init; } = Platform.ClasspathSeparator;
}

public sealed record LaunchCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>根据版本 JSON 与启动选项生成完整的 java 启动命令。</summary>
public static class CommandBuilder
{
    public static LaunchCommand Build(VersionJson version, LaunchOptions o)
    {
        var vars = BuildVariables(version, o);
        var args = new List<string>();

        args.Add($"-Xms{o.MinMemoryMb}M");
        args.Add($"-Xmx{o.MaxMemoryMb}M");
        args.AddRange(o.ExtraJvmArgs);

        if (version.Arguments?.Jvm is { Count: > 0 } jvm)
        {
            foreach (var el in jvm) args.AddRange(Expand(el, vars));
        }
        else
        {
            // 旧版本没有 jvm 参数定义，手动补齐必需项。
            args.Add(Replace("-Djava.library.path=${natives_directory}", vars));
            args.Add("-cp");
            args.Add(Replace("${classpath}", vars));
        }

        // 去掉重复的 -cp / -Djava.library.path，只保留最后出现的一组。
        args = Deduplicate(args);

        args.Add(version.MainClass);

        if (version.Arguments?.Game is { Count: > 0 } game)
        {
            foreach (var el in game) args.AddRange(Expand(el, vars));
        }
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            foreach (var part in version.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                args.Add(Replace(part, vars));
            }
        }

        return new LaunchCommand(o.JavaPath, args);
    }

    private static Dictionary<string, string> BuildVariables(VersionJson version, LaunchOptions o)
    {
        var classpath = string.Join(o.ClasspathSeparator, o.ClasspathEntries.Append(o.ClientJar));
        return new Dictionary<string, string>
        {
            ["auth_player_name"] = o.Username,
            ["version_name"] = version.Id,
            ["game_directory"] = o.GameDirectory,
            ["assets_root"] = o.AssetsDirectory,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
            ["auth_uuid"] = o.Uuid,
            ["auth_access_token"] = o.AccessToken,
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["user_type"] = o.UserType,
            ["version_type"] = "release",
            ["natives_directory"] = o.NativesDirectory,
            ["launcher_name"] = o.LauncherName,
            ["launcher_version"] = o.LauncherVersion,
            ["classpath"] = classpath,
            ["library_directory"] = o.LibrariesDirectory,
            ["classpath_separator"] = o.ClasspathSeparator,
        };
    }

    private static IEnumerable<string> Expand(JsonElement el, IReadOnlyDictionary<string, string> vars)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            yield return Replace(el.GetString()!, vars);
            yield break;
        }

        if (el.ValueKind != JsonValueKind.Object) yield break;

        if (el.TryGetProperty("rules", out var rulesEl))
        {
            var rules = JsonSerializer.Deserialize<List<Rule>>(rulesEl.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (!RuleEvaluator.IsAllowed(rules)) yield break;
        }

        if (!el.TryGetProperty("value", out var value)) yield break;

        if (value.ValueKind == JsonValueKind.String)
        {
            yield return Replace(value.GetString()!, vars);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) yield return Replace(item.GetString()!, vars);
            }
        }
    }

    private static List<string> Deduplicate(List<string> args)
    {
        // 找到最后一次出现的 "-cp" 并删除之前的同名参数对。
        var result = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-cp" && i + 1 < args.Count)
            {
                var lastCp = LastIndexOf(args, "-cp");
                if (i != lastCp)
                {
                    i++;
                    continue;
                }
            }
            result.Add(args[i]);
        }
        return result;
    }

    private static int LastIndexOf(List<string> list, string value)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == value) return i;
        }
        return -1;
    }

    private static string Replace(string input, IReadOnlyDictionary<string, string> vars)
    {
        var result = input;
        foreach (var (key, value) in vars)
        {
            result = result.Replace("${" + key + "}", value);
        }
        return result;
    }
}
