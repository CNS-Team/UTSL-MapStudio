using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Reflection;
using ThreeWorlds.Live;
namespace MapStudio;
public sealed partial class StudioWindow
{
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    Task Snapshot(string path) => Snapshot(this, path);
    static async Task Snapshot(Window target, string path)
    {
        await Task.Delay(200); double scale = target.RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(target.Bounds.Width * scale), (int)Math.Ceiling(target.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(target); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
    async Task Smoke(string directory, string? preview)
    {
        Directory.CreateDirectory(directory); var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Func<bool> body) { try { if (!body()) throw new Exception("assertion false"); checks.Add(new { name, pass = true }); passed++; } catch (Exception ex) { checks.Add(new { name, pass = false, error = ex.Message }); failed++; } }
        Check("real_avalonia_runtime", () => typeof(Window).Assembly.GetName().Name == "Avalonia.Controls");
        Check("requested_dark_theme", () => ActualThemeVariant == ThemeVariant.Dark);
        Check("window_visible", () => IsVisible && TryGetPlatformHandle()?.Handle != IntPtr.Zero);
        Check("no_console_window", () => GetConsoleWindow() == IntPtr.Zero);
        Check("default_large_master", () => Options() is { Width: 8400, Height: 2400, Difficulty: 2 });
        Check("eleven_original_styles_retained", () => StyleChoice.ItemCount == 11);
        Check("two_generation_modes", () => LayoutChoice.ItemCount == 2);
        Check("nine_environment_checks", () => features.Count == 9);
        Check("root_map_output", () => Path.GetDirectoryName(OutputPath.Text) == Program.MapsRoot);
        Check("legacy_engine_exists", () => File.Exists(Program.Runner));
        Check("top_settings_lower_preview", () => MapView.TranslatePoint(default, this)!.Value.Y > SettingsPanel.TranslatePoint(default, this)!.Value.Y + SettingsPanel.Bounds.Height);
        Check("panorama_has_sufficient_area", () => MapView.Bounds.Width >= 900 && MapView.Bounds.Height >= 300);
        Check("cancel_disabled_when_idle", () => !CancelButton.IsEnabled && GenerateButton.IsEnabled);
        Check("brief_product_title", () => Title == "UTSL地图生成器");
        Check("actual_window_icon_loaded", () => Icon != null && BrandImage.Source != null);
        Check("assembly_development_team", () => typeof(StudioWindow).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company == "流光工作室");
        Check("assembly_copyright_attribution", () => typeof(StudioWindow).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright == "© 流光-Pigeon-X");
        Check("assembly_simple_description", () => typeof(StudioWindow).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description == "UTSL地图生成器");
        Check("windows_exe_file_description", () => System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileDescription == "UTSL地图生成器");
        Check("foundation_collapsed_by_default", () => !FoundationExpander.IsExpanded);
        Check("native_catalog_bound_to_bundled_engine", () => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Program.RuntimeRoot, "engine", "OTAPI.dll")))) == SeedCatalog.Current.EngineSha256);
        Check("catalog_9_native_37_traits", () => SeedCatalog.Current.NativeSeeds.Length == 9 && SeedCatalog.Current.Traits.Length == 37);
        string chinesePath = Path.Combine(directory, "中文路径-预览管道-泰拉大陆");
        var info = CreateRunnerInfo(); info.ArgumentList.Add("ui-protocol-probe"); info.ArgumentList.Add(chinesePath);
        using (var probe = System.Diagnostics.Process.Start(info)!) {
            var outputTask = probe.StandardOutput.ReadToEndAsync(); var errorTask = probe.StandardError.ReadToEndAsync();
            await probe.WaitForExitAsync(); string protocol = await outputTask; string errors = await errorTask;
            foreach (string line in protocol.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Receive(line.TrimEnd('\r'));
            await Task.Delay(120);
            Check("actual_hidden_child_utf8_path_protocol", () => probe.ExitCode == 0 && errors.Length == 0 && previewDirectory == chinesePath);
            previewDirectory = null;
        }
        await Snapshot(Path.Combine(directory, "01-avalonia-dark-idle.png"));
        FoundationExpander.IsExpanded = true; await Task.Delay(100); Check("foundation_can_expand_without_loss", () => features.Count == 9 && FoundationChoice.ItemCount == 3); await Snapshot(Path.Combine(directory, "06-foundation-expanded.png")); FoundationExpander.IsExpanded = false;
        var about = CreateAboutWindow(); var aboutTask = about.ShowDialog(this); await Task.Delay(150);
        Check("about_only_brief_intro_and_attribution", () => about.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "开发团队：流光工作室") && about.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "版权署名：流光-Pigeon-X"));
        await Snapshot(about, Path.Combine(directory, "07-about-icon-and-attribution.png")); about.Close(); await aboutTask;
        var beforeRecipe = Options();
        var seedWindow = CreateSeedsWindow(); var seedTask = seedWindow.ShowDialog(this); await Task.Delay(150);
        T Find<T>(string name) where T : Control => seedWindow.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        Check("actual_seed_ui_has_46_checkboxes", () => seedWindow.GetVisualDescendants().OfType<CheckBox>().Count() == 46);
        Find<ComboBox>("RecipeChoice").SelectedIndex = 3;
        Find<Button>("ApplyRecipe").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("recipe_changes_actual_ui_checkboxes", () => seedWindow.GetVisualDescendants().OfType<CheckBox>().Count(b => b.IsChecked == true) == 2);
        await Snapshot(seedWindow, Path.Combine(directory, "08-seed-catalog.png"));
        Find<TextBox>("SeedSearch").Text = "浮岛";
        await Task.Delay(150);
        Check("seed_search_filters_actual_controls", () => seedWindow.GetVisualDescendants().OfType<CheckBox>().Count(b => b.IsVisible) == 1);
        Find<TextBox>("SeedSearch").Text = "";
        await Task.Delay(100);
        Find<Button>("SaveSeeds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await seedTask;
        Check("recipe_reaches_serializable_generation_request", () => Options().SeedTraits.SequenceEqual(new[] { "extraLivingTrees", "extraFloatingIslands" }) && Options().Seed == beforeRecipe.Seed && Options().Difficulty == beforeRecipe.Difficulty);
        Apply(beforeRecipe);
        var parameterWindow = CreateParametersWindow(); var parametersTask = parameterWindow.ShowDialog(this); await Task.Delay(150);
        Check("details_live_in_function_help", () => parameterWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "尺寸与难度")); await Snapshot(parameterWindow, Path.Combine(directory, "09-function-parameters.png")); parameterWindow.Close(); await parametersTask;
        LogsClick(null, null!); Check("log_menu_opens_inline", () => LogPanel.IsVisible); Append("Actual Avalonia GUI test; offline only.");
        Check("actual_log_append", () => LogText.Text!.Contains("Actual Avalonia")); await Snapshot(Path.Combine(directory, "02-dark-logs.png")); LogsClick(null, null!);
        bool previous = SeamsCheck.IsChecked == true; SeamsCheck.IsChecked = !previous;
        Check("seam_toggle_propagated", () => MapView.ShowSeams == !previous); SeamsCheck.IsChecked = previous;
        NormalClick(null, null!); Check("normal_mode_resolved", () => Options().WorldLayout == "normal-stitch"); ContinuousClick(null, null!);
        Check("continuous_mode_resolved", () => Options().WorldLayout == "continuous");
        if (preview != null && File.Exists(preview))
        {
            MapView.LoadPanorama(preview, new StudioOptions { Width = 4200, Height = 1200 }); Check("real_saved_world_panorama_loaded", () => MapView.HasFrame);
            ZoomInClick(null, null!); Check("zoom_changes_actual_canvas", () => MapView.ZoomFactor > 1); FitClick(null, null!); Check("fit_resets_zoom", () => MapView.ZoomFactor == 1);
            await Snapshot(Path.Combine(directory, "03-dark-real-panorama.png"));
        }
        SettingsClick(null, null!); await Task.Delay(100); Check("settings_collapse_keeps_large_map", () => !SettingsPanel.IsVisible && MapView.Bounds.Height > 450); await Snapshot(Path.Combine(directory, "04-dark-max-map.png")); SettingsClick(null, null!);
        Width = 1100; Height = 760; await Task.Delay(200); Check("minimum_window_usable", () => MapView.Bounds.Width >= 1000 && MapView.Bounds.Height >= 210); await Snapshot(Path.Combine(directory, "05-dark-minimum.png"));
        Data.Write(Path.Combine(directory, "result.json"), new { passed, failed, checks, product = Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.MapStudio.dll")),
            framework = typeof(Window).Assembly.FullName, mapsRoot = Program.MapsRoot, rootLayout = "top left/right menus and settings; map underneath", productionTouched = false, gameE2E = "UNVERIFIED", at = DateTimeOffset.Now });
        if (failed != 0 && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Exit += (_, e) => e.ApplicationExitCode = 1;
    }
    static object Inspect(string path) => new { path, bytes = new FileInfo(path).Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) };
}
