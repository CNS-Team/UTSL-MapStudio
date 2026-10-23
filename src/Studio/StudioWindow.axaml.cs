using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThreeWorlds.Live;
namespace MapStudio;
public sealed partial class StudioWindow : Window
{
    readonly Dictionary<string, CheckBox> features = [];
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(400) };
    Process? runner; string? cancelFile, previewDirectory, source; StudioOptions? active; bool polling, closeAfterCancel;
    int frames; readonly Stopwatch elapsed = new();
    string[] nativeSeeds = [], seedTraits = [];
    public StudioWindow()
    {
        InitializeComponent();
        StyleChoice.ItemsSource = StyleCatalog.All.Select(s => s.Name).ToArray(); StyleChoice.SelectedIndex = 0;
        LayoutChoice.ItemsSource = new[] { "连续母图 · 整体切片", "独立标准三世界 · 同海平面" }; LayoutChoice.SelectedIndex = 0;
        SizeChoice.ItemsSource = new[] { "大世界 8400 × 2400", "中世界 6400 × 1500", "小世界 4200 × 1200" }; SizeChoice.SelectedIndex = 0;
        DifficultyChoice.ItemsSource = new[] { "普通", "专家", "大师", "旅行" }; DifficultyChoice.SelectedIndex = 2;
        FoundationChoice.ItemsSource = new[] { "空岛自动补全", "自定义补全", "保持纯原生" }; FoundationChoice.SelectedIndex = 0;
        RefreshChoice.ItemsSource = new[] { "0.5 秒", "1 秒（推荐）", "2 秒", "5 秒" }; RefreshChoice.SelectedIndex = 1;
        foreach (var (id, label) in new[] { ("dungeon", "地牢"), ("temple", "神庙"), ("jungle", "丛林"), ("crimson", "猩红"), ("corruption", "腐化"), ("shimmer", "微光"), ("snow", "雪原"), ("desert", "沙漠"), ("mushroom", "发光蘑菇") })
        { var box = new CheckBox { Content = label, IsChecked = new FoundationSettings().Features.Contains(id), Margin = new Thickness(0, 0, 16, 0) }; features[id] = box; FeatureChecks.Children.Add(box); }
        OutputPath.Text = Data.NewOutput();
        ConfigureParameterTips(); UpdateSeedSummary();
        MapView.PositionChanged = text => FooterText.Text = text;
        SeamsCheck.IsCheckedChanged += (_, _) => { MapView.ShowSeams = SeamsCheck.IsChecked == true; MapView.InvalidateVisual(); };
        LayoutChoice.SelectionChanged += (_, _) => { ModeHint.Text = LayoutChoice.SelectedIndex == 1 ? "三张独立标准地图 · 同Y海平面接缝" : "1 西  ↔  2 中（默认） ↔  3 东"; };
        poll.Tick += async (_, _) => await Poll(); poll.Start();
        Closing += (_, e) => { if (runner != null) { e.Cancel = true; closeAfterCancel = true; RequestCancel(); } };
        Closed += (_, _) => poll.Stop();
        Opened += async (_, _) =>
        {
            await Task.Delay(450);
            if (Program.Args.Length >= 2 && Program.Args[0] == "--ui-smoke") { await Smoke(Program.Args[1], Program.Args.Length > 2 ? Program.Args[2] : null); Close(); }
            if (Program.Args.Length >= 2 && Program.Args[0] == "--ui-generate")
            {
                var options = JsonSerializer.Deserialize<StudioOptions>(File.ReadAllText(Program.Args[1]), Data.Json)!; Apply(options);
                string evidence = Program.Args.Length > 2 ? Program.Args[2] : Path.Combine(Program.RuntimeRoot, "运行记录", "ui-test");
                Directory.CreateDirectory(evidence); await Snapshot(Path.Combine(evidence, "01-open.png"));
                bool success = await Generate(); await Snapshot(Path.Combine(evidence, "99-result.png"));
                Data.Write(Path.Combine(evidence, "result.json"), new { success, livePreviewPassed = !options.LivePreview || options.Source != null || frames > 0, actualPreviewFramesDisplayed = frames, options, elapsedSeconds = elapsed.Elapsed.TotalSeconds, productionTouched = false, gameE2E = "UNVERIFIED", pid = Environment.ProcessId }); Close();
            }
        };
    }
    StudioOptions Options()
    {
        var dims = SizeChoice.SelectedIndex == 0 ? (8400, 2400) : SizeChoice.SelectedIndex == 1 ? (6400, 1500) : (4200, 1200);
        return new() { Name = WorldName.Text?.Trim() ?? "", Seed = Seed.Text?.Trim() ?? "", Width = dims.Item1, Height = dims.Item2, Difficulty = DifficultyChoice.SelectedIndex,
            Style = StyleCatalog.All[StyleChoice.SelectedIndex].Id, WorldLayout = LayoutChoice.SelectedIndex == 1 ? "normal-stitch" : "continuous", Source = source, NativeSeeds = nativeSeeds.ToArray(), SeedTraits = seedTraits.ToArray(),
            LivePreview = LiveCheck.IsChecked == true, PreviewIntervalMs = new[] { 500, 1000, 2000, 5000 }[RefreshChoice.SelectedIndex], Output = OutputPath.Text?.Trim() ?? "",
            Foundations = new() { Mode = new[] { "skyblock-auto", "enabled", "disabled" }[FoundationChoice.SelectedIndex], Features = features.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray() } };
    }
    void Apply(StudioOptions o)
    {
        WorldName.Text = o.Name; Seed.Text = o.Seed; SizeChoice.SelectedIndex = o.Width == 8400 ? 0 : o.Width == 6400 ? 1 : 2; DifficultyChoice.SelectedIndex = o.Difficulty;
        nativeSeeds = o.NativeSeeds.ToArray(); seedTraits = o.SeedTraits.ToArray(); UpdateSeedSummary();
        StyleChoice.SelectedIndex = Array.FindIndex(StyleCatalog.All, s => s.Id == o.Style); LayoutChoice.SelectedIndex = o.WorldLayout == "normal-stitch" ? 1 : 0;
        LiveCheck.IsChecked = o.LivePreview; OutputPath.Text = o.Output; source = o.Source; ImportButton.Content = source == null ? "导入母图…" : "已导入母图";
        FoundationChoice.SelectedIndex = Array.IndexOf(new[] { "skyblock-auto", "enabled", "disabled" }, o.Foundations.Mode);
        RefreshChoice.SelectedIndex = Array.IndexOf(new[] { 500, 1000, 2000, 5000 }, o.PreviewIntervalMs);
        foreach (var f in features) f.Value.IsChecked = o.Foundations.Features.Contains(f.Key);
    }
    void Append(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Append(text)); return; }
        string current = LogText.Text ?? ""; if (current.Length > 100_000) current = current[^60_000..]; LogText.Text = current + $"[{DateTime.Now:HH:mm:ss}] {text}\n"; LogText.CaretIndex = LogText.Text.Length;
    }
    void Receive(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (line.StartsWith("UI_PREVIEW ")) { using var d = JsonDocument.Parse(line[11..]); previewDirectory = d.RootElement.GetProperty("directory").GetString(); MapView.Clear(); MapView.ShowSeams = d.RootElement.GetProperty("mother").GetBoolean() && SeamsCheck.IsChecked == true; return; }
                if (line.StartsWith("UI_STAGE ")) { using var d = JsonDocument.Parse(line[9..]); StatusText.Text = d.RootElement.GetProperty("text").GetString(); return; }
            }
            catch (JsonException) { }
            Append(line);
        });
    }
    static ProcessStartInfo CreateRunnerInfo() => new(Program.Runner) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
    async Task<bool> Generate()
    {
        if (runner != null) return false; bool success = false;
        try
        {
            var o = Options();
            if (string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(o.Output)) throw new ArgumentException("请填写世界名称和输出目录。");
            SeedCatalog.ValidateForStyle(o.Style, o.NativeSeeds, o.SeedTraits);
            if (o.WorldLayout == "normal-stitch" && (o.Style != "classic" || o.Source != null || o.Foundations.Mode == "enabled" || o.NativeSeeds.Length > 0 || o.SeedTraits.Length > 0)) throw new ArgumentException("独立标准三世界仅用于正常原版地形；特殊样式/种子特性/导入母图/自定义补全请选连续母图。");
            if (!File.Exists(Program.Runner)) throw new FileNotFoundException("本地生成引擎缺失。", Program.Runner);
            string job = Path.Combine(Program.RuntimeRoot, "运行记录", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(job);
            string request = Path.Combine(job, "request.json"); cancelFile = Path.Combine(job, "cancel.request"); Data.Write(request, o);
            var psi = CreateRunnerInfo();
            psi.ArgumentList.Add("generate"); psi.ArgumentList.Add(request); psi.ArgumentList.Add(cancelFile);
            active = o; frames = 0; MapView.Clear(); MapView.Configure(o); previewDirectory = null; elapsed.Restart();
            GenerateButton.IsEnabled = false; CancelButton.IsEnabled = true; SettingsPanel.IsEnabled = false; NativeProgress.Value = 0;
            runner = Process.Start(psi) ?? throw new IOException("无法启动本地生成引擎。");
            Data.Write(Path.Combine(job, "launch.json"), new { pid = runner.Id, path = Program.Runner, runner.StartTime, productionTouched = false, cancellationPolicy = "request-file -> runner token -> only owned worker PID; no process-name/tree kills" });
            using var stdout = new StreamWriter(Path.Combine(job, "stdout.log")); using var stderr = new StreamWriter(Path.Combine(job, "stderr.log"));
            async Task Drain(StreamReader stream, StreamWriter file) { while (await stream.ReadLineAsync() is { } line) { await file.WriteLineAsync(line); await file.FlushAsync(); Receive(line); } }
            var outTask = Drain(runner.StandardOutput, stdout); var errTask = Drain(runner.StandardError, stderr);
            await runner.WaitForExitAsync(); await Task.WhenAll(outTask, errTask); await Poll();
            success = runner.ExitCode == 0 && File.Exists(Path.Combine(o.Output, "COMPLETE.json"));
            if (success)
            {
                MapView.LoadPanorama(Path.Combine(o.Output, "三联世界长图.png"), o); MapView.ShowSeams = SeamsCheck.IsChecked == true; NativeProgress.Value = 100;
                StatusText.Text = "生成完成 · 三张地图与接缝已验证，长图已附带"; FrameText.Text = "可在地图文件夹查看 .wld、三联世界长图与校验记录；真人切换/渲染未验证。";
            }
            else { StatusText.Text = File.Exists(cancelFile) ? "已取消 · 不完整目录不能部署" : "生成失败 · 请查看运行日志"; LogPanel.IsVisible = true; }
            Data.Write(Path.Combine(job, "result.json"), new { success, exitCode = runner.ExitCode, o.Output, framesDisplayed = frames, elapsedSeconds = elapsed.Elapsed.TotalSeconds, productionTouched = false });
        }
        catch (Exception ex) { StatusText.Text = "未完成 · " + ex.Message; Append(ex.ToString()); LogPanel.IsVisible = true; }
        finally
        {
            elapsed.Stop(); runner?.Dispose(); runner = null; active = null; cancelFile = null; GenerateButton.IsEnabled = true; CancelButton.IsEnabled = false; SettingsPanel.IsEnabled = true;
            ProgressText.Text = "用时 " + elapsed.Elapsed.ToString(@"hh\:mm\:ss"); if (closeAfterCancel) Close();
        }
        return success;
    }
    async Task Poll()
    {
        if (polling || runner == null || previewDirectory == null) return; polling = true;
        try
        {
            string dir = previewDirectory; long sequence = MapView.Sequence;
            var data = await Task.Run(() => { var s = LiveProtocol.ReadStatus(Path.Combine(dir, "live-status.json")); var f = s != null && s.Sequence != sequence && File.Exists(Path.Combine(dir, "live-frame.bin")) ? LiveProtocol.ReadFrame(Path.Combine(dir, "live-frame.bin")) : null; return (s, f); });
            if (previewDirectory != dir) return;
            if (data.f != null) { MapView.SetFrame(data.f); frames++; }
            if (data.s is { } s) { NativeProgress.Value = s.NativeProgress * 100; ProgressText.Text = $"当前图原生 {s.NativeProgress:P1}  ·  " + elapsed.Elapsed.ToString(@"hh\:mm\:ss"); FrameText.Text = $"{s.Pass} / {s.Message}  ·  真实图格帧 {s.Frames}  ·  {s.FrameWidth}×{s.FrameHeight}"; }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        finally { polling = false; }
    }
    void RequestCancel() { if (runner != null && cancelFile != null) { File.WriteAllText(cancelFile, "cancel-owned-generation"); CancelButton.IsEnabled = false; StatusText.Text = "正在取消本地生成，请等待所属worker退出…"; } }
    void DragWindow(object? sender, PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }
    void CloseClick(object? s, RoutedEventArgs e) => Close();
    void MinimizeClick(object? s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeClick(object? s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void FitClick(object? s, RoutedEventArgs e) => MapView.Fit();
    void ZoomInClick(object? s, RoutedEventArgs e) => MapView.Zoom(1.3);
    void ZoomOutClick(object? s, RoutedEventArgs e) => MapView.Zoom(1 / 1.3);
    void LogsClick(object? s, RoutedEventArgs e) => LogPanel.IsVisible = !LogPanel.IsVisible;
    void SettingsClick(object? s, RoutedEventArgs e) => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    void ContinuousClick(object? s, RoutedEventArgs e) { if (runner == null) LayoutChoice.SelectedIndex = 0; }
    void NormalClick(object? s, RoutedEventArgs e) { if (runner == null) LayoutChoice.SelectedIndex = 1; }
    void NewClick(object? s, RoutedEventArgs e) { if (runner != null) return; OutputPath.Text = Data.NewOutput(); source = null; ImportButton.Content = "导入母图…"; MapView.Clear(); }
    void CancelClick(object? s, RoutedEventArgs e) => RequestCancel();
    async void GenerateClick(object? s, RoutedEventArgs e) => await Generate();
    void MapsClick(object? s, RoutedEventArgs e) { Directory.CreateDirectory(Program.MapsRoot); Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { Program.MapsRoot } }); }
    async void ImportClick(object? s, RoutedEventArgs e)
    {
        if (runner != null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "导入连续母图", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Terraria 世界") { Patterns = ["*.wld"] }] });
        if (files.Count > 0) { source = files[0].TryGetLocalPath(); ImportButton.Content = "已导入母图"; LayoutChoice.SelectedIndex = 0; Append("母图：" + source); }
    }
    async void OutputClick(object? s, RoutedEventArgs e)
    {
        if (runner != null) return; Directory.CreateDirectory(Program.MapsRoot);
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择新的空输出文件夹", AllowMultiple = false, SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Program.MapsRoot) });
        if (folders.Count > 0) OutputPath.Text = folders[0].TryGetLocalPath();
    }
}
