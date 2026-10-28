using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Launcher.Core;

namespace Launcher.Desktop;

/// <summary>下拉框条目：显示文本 + 实际值。</summary>
public sealed record Choice(string Text, object? Value)
{
    public override string ToString() => Text;
}

public partial class MainWindow : Window
{
    private static readonly Regex OfflineNamePattern = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private readonly LaunchService _launch;
    private readonly IProgress<string> _progress;
    private List<ManifestVersion> _allVersions = new();
    private CancellationTokenSource? _cts;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        _launch = new LaunchService(GameRootPath());
        _progress = new Progress<string>(Log);

        UserNameBox.Text = "Player";
        MemoryBox.ItemsSource = new[] { 1024, 2048, 4096, 6144 }
            .Select(m => new Choice($"{m} MB", m)).ToList();
        MemoryBox.SelectedIndex = 1;

        LoaderBox.ItemsSource = new[]
        {
            new Choice("原版", LoaderKind.Vanilla),
            new Choice("Fabric", LoaderKind.Fabric),
            new Choice("Quilt", LoaderKind.Quilt),
            new Choice("Forge", LoaderKind.Forge),
            new Choice("NeoForge", LoaderKind.NeoForge),
        };
        LoaderBox.SelectedIndex = 0;

        ReleaseOnlyBox.IsCheckedChanged += (_, _) => { if (_ready) ApplyVersionFilter(); };
        VersionBox.SelectionChanged += async (_, _) => { if (_ready) await RefreshLoaderBuildsAsync(); };
        LoaderBox.SelectionChanged += async (_, _) => { if (_ready) await RefreshLoaderBuildsAsync(); };

        Opened += async (_, _) => await InitializeAsync();
    }

    private static string GameRootPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McLauncher");

    private async Task InitializeAsync()
    {
        Log($"游戏目录：{_launch.GameRoot}");
        await RefreshJavaAsync();
        await LoadManifestAsync();
        _ready = true;
        await RefreshLoaderBuildsAsync();
    }

    private async Task LoadManifestAsync()
    {
        try
        {
            SetStatus("正在获取版本清单…");
            var manifest = await _launch.Versions.GetManifestAsync();
            _allVersions = manifest.Versions;
            ApplyVersionFilter(manifest.Latest.Release);
            Log($"已加载 {_allVersions.Count} 个版本（镜像优先）");
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            Log($"版本清单加载失败：{ex.Message}");
            SetStatus("版本清单加载失败，请检查网络后重启");
        }
    }

    private void ApplyVersionFilter(string? preferredId = null)
    {
        var releaseOnly = ReleaseOnlyBox.IsChecked == true;
        var items = _allVersions
            .Where(v => !releaseOnly || v.Type == "release")
            .Select(v => new Choice(v.Id, v.Id))
            .ToList();

        var current = SelectedVersionId();
        VersionBox.ItemsSource = items;
        VersionBox.SelectedItem = items.FirstOrDefault(c => (string?)c.Value == (preferredId ?? current))
                                  ?? items.FirstOrDefault();
    }

    private async Task RefreshJavaAsync()
    {
        Log("检测 Java…");
        var found = await JavaLocator.FindAsync();
        var items = new List<Choice> { new("自动（推荐：按版本需要下载合适的 Java）", null) };
        items.AddRange(found.Select(j => new Choice($"Java {j.MajorVersion} · {j.Path}", j)));
        JavaBox.ItemsSource = items;
        JavaBox.SelectedIndex = 0;
        Log(found.Count == 0
            ? "未检测到本机 Java，将按需自动下载"
            : $"检测到 {found.Count} 个本机 Java，默认使用自动选择");
    }

    private async Task RefreshLoaderBuildsAsync()
    {
        var kind = Selected<LoaderKind>(LoaderBox);
        var mc = SelectedVersionId();
        LoaderVersionBox.ItemsSource = null;

        if (kind == LoaderKind.Vanilla || mc is null)
        {
            LoaderVersionBox.IsEnabled = false;
            return;
        }

        try
        {
            SetStatus($"正在获取 {kind} 版本列表…");
            var builds = await LoaderCatalog.GetBuildsAsync(kind, mc);
            var items = builds.Select(b => new Choice(b.Version, b)).ToList();
            LoaderVersionBox.ItemsSource = items;
            LoaderVersionBox.SelectedIndex = items.Count > 0 ? 0 : -1;
            LoaderVersionBox.IsEnabled = true;
            SetStatus(items.Count == 0 ? $"{mc} 暂无 {kind} 版本" : "就绪");
        }
        catch (Exception ex)
        {
            LoaderVersionBox.IsEnabled = false;
            Log($"获取 {kind} 版本列表失败：{ex.Message}");
            SetStatus("加载器列表获取失败");
        }
    }

    private async void OnRefreshJava(object? sender, RoutedEventArgs e) => await RefreshJavaAsync();

    private async void OnLaunch(object? sender, RoutedEventArgs e)
    {
        var mc = SelectedVersionId();
        var kind = Selected<LoaderKind>(LoaderBox);
        var build = Selected<LoaderBuild>(LoaderVersionBox);
        var manualJava = Selected<JavaInstall>(JavaBox);
        var name = (UserNameBox.Text ?? "").Trim();
        var memory = Selected<int>(MemoryBox);

        if (mc is null)
        {
            SetStatus("请选择游戏版本");
            return;
        }
        if (kind != LoaderKind.Vanilla && build is null)
        {
            SetStatus($"请选择 {kind} 版本");
            return;
        }
        if (!OfflineNamePattern.IsMatch(name))
        {
            SetStatus("玩家名需为 3-16 位字母、数字或下划线");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            Log($"开始准备 {mc} {(kind == LoaderKind.Vanilla ? "原版" : kind + " " + build?.Version)}");
            SetStatus("正在安装，请稍候…");

            var installed = await _launch.PrepareAsync(kind, mc, build, _progress, _cts.Token);

            var required = installed.Json.RequiredJavaMajor;
            var javaPath = await _launch.ResolveJavaAsync(required, manualJava, _progress, _cts.Token);

            var command = GameLauncher.CreateCommand(installed, javaPath, name, _launch.GameRoot, memory);
            StartGame(command);
            SetStatus($"已启动 {installed.Json.Id}");
        }
        catch (OperationCanceledException)
        {
            Log("已取消");
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            Log($"失败：{ex.Message}");
            SetStatus("失败，详见日志");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var dir = _launch.GameRoot;
        Directory.CreateDirectory(dir);
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", dir)
                : new ProcessStartInfo("xdg-open", dir);
        try { Process.Start(psi); }
        catch (Exception ex) { Log($"无法打开目录：{ex.Message}"); }
    }

    private void StartGame(LaunchCommand command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = _launch.GameRoot,
            UseShellExecute = false,
        };
        // ArgumentList 由系统负责转义，避免手工拼接引号出错。
        foreach (var arg in command.Arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        Log($"游戏进程已启动（PID {process?.Id}）");
    }

    private void SetBusy(bool busy)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LaunchButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            VersionBox.IsEnabled = !busy;
            LoaderBox.IsEnabled = !busy;
            LoaderVersionBox.IsEnabled = !busy && LoaderVersionBox.ItemCount > 0 && Selected<LoaderKind>(LoaderBox) != LoaderKind.Vanilla;
        });
    }

    private string? SelectedVersionId() => Selected<string>(VersionBox);

    private static T? Selected<T>(ComboBox box) => (box.SelectedItem as Choice)?.Value is T v ? v : default;

    private void SetStatus(string text) => StatusText.Text = text;

    private void Log(string message)
    {
        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogScroll.ScrollToEnd();
    }
}
