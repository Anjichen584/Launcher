using System.Diagnostics;
using Launcher.Core;

namespace Launcher.Core.Tests;

/// <summary>
/// 真实网络与真实 Java 的集成测试。
/// RUN_NETWORK_TESTS=1 时运行 Java 下载与 Fabric；RUN_FORGE_TEST=1 时额外运行 Forge 安装器（耗时较长）。
/// </summary>
public class LoaderNetworkTests
{
    // /tmp 在部分环境是小容量内存盘，集成测试需要数 GB，放到 ~/.cache 下。
    private static string NewTempRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "mc-it", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task JavaManager_Downloads_Java17_AndItRuns()
    {
        if (Environment.GetEnvironmentVariable("RUN_NETWORK_TESTS") != "1") return;

        var root = NewTempRoot();
        try
        {
            var java = new JavaManager(root);
            var exe = await java.EnsureAsync(17);
            Assert.True(File.Exists(exe));

            var psi = new ProcessStartInfo(exe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardError.ReadToEndAsync() + await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            Assert.Equal(17, JavaLocator.ParseMajorVersion(output));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Fabric_1_20_1_InstallsAndBuildsLaunchCommand()
    {
        if (Environment.GetEnvironmentVariable("RUN_NETWORK_TESTS") != "1") return;

        var root = NewTempRoot();
        try
        {
            var builds = await LoaderCatalog.GetBuildsAsync(LoaderKind.Fabric, "1.20.1");
            Assert.NotEmpty(builds);

            var launch = new LaunchService(root);
            var installed = await launch.PrepareAsync(LoaderKind.Fabric, "1.20.1", builds[0]);

            Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", installed.Json.MainClass);
            Assert.True(File.Exists(installed.ClientJar));
            Assert.Contains(installed.ClasspathEntries, p => p.Contains("fabric-loader"));
            Assert.Equal(17, installed.Json.RequiredJavaMajor);

            var cmd = GameLauncher.CreateCommand(installed, "java", "Player", root);
            Assert.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", cmd.Arguments);
            Assert.DoesNotContain(cmd.Arguments, a => a.Contains("${"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Forge_1_20_1_RunsInstallerAndPrepares()
    {
        if (Environment.GetEnvironmentVariable("RUN_FORGE_TEST") != "1") return;

        var root = NewTempRoot();
        try
        {
            var builds = await LoaderCatalog.GetBuildsAsync(LoaderKind.Forge, "1.20.1");
            Assert.NotEmpty(builds);

            var launch = new LaunchService(root);
            var installed = await launch.PrepareAsync(LoaderKind.Forge, "1.20.1", builds[0]);

            Assert.False(string.IsNullOrEmpty(installed.Json.MainClass));
            Assert.True(File.Exists(installed.ClientJar));
            Assert.NotEmpty(installed.ClasspathEntries);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NeoForge_1_21_1_RunsInstallerAndPrepares()
    {
        if (Environment.GetEnvironmentVariable("RUN_FORGE_TEST") != "1") return;

        var root = NewTempRoot();
        try
        {
            var builds = await LoaderCatalog.GetBuildsAsync(LoaderKind.NeoForge, "1.21.1");
            Assert.NotEmpty(builds);
            Assert.False(string.IsNullOrEmpty(builds[0].InstallerPath));

            var launch = new LaunchService(root);
            var installed = await launch.PrepareAsync(LoaderKind.NeoForge, "1.21.1", builds[0]);

            Assert.False(string.IsNullOrEmpty(installed.Json.MainClass));
            Assert.True(File.Exists(installed.ClientJar));
            Assert.NotEmpty(installed.ClasspathEntries);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
