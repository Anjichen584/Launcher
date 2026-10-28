using Launcher.Core;

namespace Launcher.Core.Tests;

/// <summary>
/// 真实网络冒烟测试：需要设置环境变量 RUN_NETWORK_TESTS=1，否则直接通过（不计入离线测试）。
/// 会真实下载一个较小的旧版本到临时目录。
/// </summary>
public class NetworkSmokeTests
{
    [Fact]
    public async Task InstallSmallVersion_AndBuildLaunchCommand()
    {
        if (Environment.GetEnvironmentVariable("RUN_NETWORK_TESTS") != "1") return;

        var root = Path.Combine(Path.GetTempPath(), "mc-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new VersionService(root);
            var installed = await service.InstallVanillaAsync("1.12.2");

            Assert.True(File.Exists(installed.ClientJar));
            Assert.NotEmpty(installed.ClasspathEntries);
            Assert.True(Directory.GetFiles(installed.NativesDirectory, "*.dll").Length > 0,
                "应当从 natives 库中解压出 Windows 动态库");
            Assert.True(Directory.Exists(Path.Combine(installed.AssetsDirectory, "objects")));

            var cmd = GameLauncher.CreateCommand(installed, @"C:\Java\bin\java.exe", "Player", root);
            Assert.Equal("net.minecraft.client.main.Main", installed.Json.MainClass);
            Assert.Contains("Player", cmd.Arguments);
            Assert.Equal(8, installed.Json.RequiredJavaMajor);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
        }
    }
}
