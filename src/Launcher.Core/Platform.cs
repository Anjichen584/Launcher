using System.Runtime.InteropServices;

namespace Launcher.Core;

/// <summary>当前运行平台的信息，用于规则判断、natives 选择和 Java 可执行文件名。</summary>
public static class Platform
{
    /// <summary>Mojang 版本 JSON 使用的 OS 名称：windows / osx / linux。</summary>
    public static string OsName =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "osx" : "linux";

    /// <summary>Adoptium API 使用的 OS 名称：windows / mac / linux。</summary>
    public static string AdoptiumOs =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "mac" : "linux";

    /// <summary>Mojang 使用的架构名：x86_64 对应 "x64"，ARM64 对应 "aarch64"。</summary>
    public static string Arch =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";

    /// <summary>Adoptium 使用的架构名。</summary>
    public static string AdoptiumArch => Arch == "aarch64" ? "aarch64" : "x64";

    /// <summary>旧版 natives 的 ${arch} 替换值。</summary>
    public static string ArchBits => "64";

    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static string ClasspathSeparator => IsWindows ? ";" : ":";

    public static string JavaExecutableName => IsWindows ? "java.exe" : "java";

    public static bool IsNativeBinary(string fileName) =>
        fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase);
}
