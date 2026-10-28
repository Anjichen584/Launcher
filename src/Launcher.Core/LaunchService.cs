namespace Launcher.Core;

/// <summary>面向界面的统一入口：准备版本（原版或加载器）并解析 Java。</summary>
public sealed class LaunchService
{
    public LaunchService(string gameRoot)
    {
        GameRoot = gameRoot;
        Versions = new VersionService(gameRoot);
        Java = new JavaManager(gameRoot);
        Loaders = new LoaderInstaller(Versions, Java);
    }

    public string GameRoot { get; }
    public VersionService Versions { get; }
    public JavaManager Java { get; }
    public LoaderInstaller Loaders { get; }

    /// <summary>安装/校验所选版本的全部文件，返回可直接启动的信息。</summary>
    public Task<InstalledVersion> PrepareAsync(
        LoaderKind kind,
        string mcVersion,
        LoaderBuild? build,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (kind == LoaderKind.Vanilla)
        {
            return Versions.InstallVanillaAsync(mcVersion, progress, ct);
        }

        if (build is null) throw new ArgumentNullException(nameof(build), "请选择加载器版本");
        return Loaders.InstallAsync(kind, mcVersion, build, progress, ct);
    }

    /// <summary>
    /// 选择 Java：手动选择且主版本足够则使用它；否则自动下载所需的主版本。
    /// </summary>
    public async Task<string> ResolveJavaAsync(
        int requiredMajor,
        JavaInstall? manual,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (manual is not null && manual.MajorVersion >= requiredMajor && File.Exists(manual.Path))
        {
            return manual.Path;
        }
        return await Java.EnsureAsync(requiredMajor, progress, ct);
    }
}
