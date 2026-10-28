using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Launcher.Core;

namespace Launcher.Wpf;

public partial class MainWindow : Window
{
    private static readonly Regex OfflineNamePattern = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    private readonly string _gameRoot;
    private readonly VersionService _versions;
    private readonly IProgress<string> _progress;
    private List<ManifestVersion> _allVersions = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();

        _gameRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "McLauncher");
        Directory.CreateDirectory(_gameRoot);

        _versions = new VersionService(_gameRoot);
        _progress = new Progress<string>(Log); // 在 UI 线程上回调

        UserNameBox.Text = "Player";
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        Log($"游戏目录：{_gameRoot}");
        await RefreshJavaAsync();
        await LoadManifestAsync();
    }

    private async Task LoadManifestAsync()
    {
        try
        {
            SetStatus("正在获取版本清单…");
            var manifest = await _versions.GetManifestAsync();
            _allVersions = manifest.Versions;
            ApplyVersionFilter(manifest.Latest.Release);
            Log($"已加载 {_allVersions.Count} 个版本（镜像优先）");
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            Log($"版本清单加载失败：{ex.Message}");
            SetStatus("版本清单加载失败，可检查网络后重启");
        }
    }

    private void ApplyVersionFilter(string? preferredId = null)
    {
        var releaseOnly = ReleaseOnlyBox.IsChecked == true;
        var list = _allVersions
            .Where(v => !releaseOnly || v.Type == "release")
            .ToList();

        VersionBox.ItemsSource = list;
        VersionBox.SelectedItem = list.FirstOrDefault(v => v.Id == preferredId) ?? list.FirstOrDefault();
    }

    private async Task RefreshJavaAsync()
    {
        Log("检测 Java…");
        var list = await JavaLocator.FindAsync();
        JavaBox.ItemsSource = list;
        JavaBox.SelectedIndex = list.Count > 0 ? 0 : -1;
        Log(list.Count == 0
            ? "未找到 Java，请先安装 Java 17 或 21（1.20.5+ 需要 21）"
            : $"检测到 {list.Count} 个 Java，已选择 Java {list[0].MajorVersion}");
    }

    private void ReleaseOnlyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyVersionFilter(((ManifestVersion?)VersionBox.SelectedItem)?.Id);
    }

    private async void RefreshJava_Click(object sender, RoutedEventArgs e)
    {
        await RefreshJavaAsync();
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        var entry = VersionBox.SelectedItem as ManifestVersion;
        var java = JavaBox.SelectedItem as JavaInstall;
        var name = UserNameBox.Text.Trim();

        if (entry is null)
        {
            MessageBox.Show(this, "请选择一个版本。", "McLauncher");
            return;
        }
        if (!OfflineNamePattern.IsMatch(name))
        {
            MessageBox.Show(this, "玩家名需为 3-16 位字母、数字或下划线（离线模式）。", "McLauncher");
            return;
        }
        if (java is null)
        {
            MessageBox.Show(this, "未找到可用的 Java，请先安装 Java 后点击“重新检测”。", "McLauncher");
            return;
        }

        var memory = int.Parse(((ComboBoxItem)MemoryBox.SelectedItem).Content.ToString()!);

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            Log($"开始安装 {entry.Id}");
            SetStatus($"正在安装 {entry.Id}…");
            var installed = await _versions.InstallAsync(entry, _progress, _cts.Token);

            var required = installed.Json.RequiredJavaMajor;
            if (java.MajorVersion < required)
            {
                Log($"版本 {entry.Id} 需要 Java {required}+，当前选择的是 Java {java.MajorVersion}");
                MessageBox.Show(this,
                    $"版本 {entry.Id} 需要 Java {required} 或更高版本，请在上方选择合适的 Java。",
                    "McLauncher");
                SetStatus("已取消启动");
                return;
            }

            var command = GameLauncher.CreateCommand(installed, java.Path, name, _gameRoot, memory);
            StartGame(command);
            SetStatus($"已启动 {entry.Id}");
        }
        catch (OperationCanceledException)
        {
            Log("已取消");
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            Log($"失败：{ex.Message}");
            SetStatus("失败");
            MessageBox.Show(this, ex.Message, "McLauncher");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void StartGame(LaunchCommand command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = _gameRoot,
            UseShellExecute = false,
        };
        // 使用 ArgumentList 由系统负责参数转义，避免手工拼接引号出错。
        foreach (var arg in command.Arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        Log($"游戏进程已启动（PID {process?.Id}）");
    }

    private void SetBusy(bool busy)
    {
        LaunchButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        RefreshJavaButton.IsEnabled = !busy;
        VersionBox.IsEnabled = !busy;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
