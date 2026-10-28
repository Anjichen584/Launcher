namespace Launcher.Core;

/// <summary>把"已安装的版本 + 账号 + Java"组装成可执行的启动命令。</summary>
public static class GameLauncher
{
    public static LaunchCommand CreateCommand(
        InstalledVersion installed,
        string javaPath,
        string username,
        string gameRoot,
        int maxMemoryMb = 2048)
    {
        var options = new LaunchOptions
        {
            JavaPath = javaPath,
            GameDirectory = gameRoot,
            AssetsDirectory = installed.AssetsDirectory,
            LibrariesDirectory = installed.LibrariesDirectory,
            NativesDirectory = installed.NativesDirectory,
            ClientJar = installed.ClientJar,
            ClasspathEntries = installed.ClasspathEntries,
            Username = username,
            Uuid = OfflineAccount.UuidFor(username),
            MinMemoryMb = Math.Min(512, maxMemoryMb),
            MaxMemoryMb = maxMemoryMb,
        };
        return CommandBuilder.Build(installed.Json, options);
    }
}
