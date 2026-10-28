namespace Launcher.Core;

/// <summary>把 Maven 坐标（group:artifact:version[:classifier]）转换为仓库相对路径。</summary>
public static class MavenCoordinates
{
    public static string ToRelativePath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) throw new FormatException($"非法的 Maven 坐标: {name}");

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }
}
