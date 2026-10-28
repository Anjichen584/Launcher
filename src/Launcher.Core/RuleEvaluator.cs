namespace Launcher.Core;

/// <summary>
/// 评估 Mojang 版本 JSON 中的 rules。目标平台为 Windows x64，
/// 启动器不启用任何可选 feature（如 demo 用户、自定义分辨率）。
/// </summary>
public static class RuleEvaluator
{
    public const string TargetOs = "windows";

    /// <summary>无 rules 视为允许；否则按顺序评估，最后一条匹配的规则决定结果。</summary>
    public static bool IsAllowed(IReadOnlyList<Rule>? rules)
    {
        if (rules is null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule)) continue;
            allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static bool Matches(Rule rule)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name)
                && !string.Equals(rule.Os.Name, TargetOs, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(rule.Os.Arch) && rule.Os.Arch != "x64")
            {
                return false;
            }
        }

        // 启动器不支持任何可选 feature：要求某 feature 为 true 的规则不匹配。
        if (rule.Features is not null && rule.Features.Values.Any(v => v))
        {
            return false;
        }

        return true;
    }
}
