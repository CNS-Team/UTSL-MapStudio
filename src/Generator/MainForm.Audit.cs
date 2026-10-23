namespace ThreeWorlds;

internal sealed partial class MainForm
{
    internal void AuditLayout(string evidence, string? preview)
    {
        Directory.CreateDirectory(evidence); var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string id, Func<bool> test)
        {
            try { if (!test()) throw new Exception("assertion false"); checks.Add(new { id, pass = true }); passed++; }
            catch (Exception ex) { checks.Add(new { id, pass = false, error = ex.Message }); failed++; }
        }
        void Snap(string id) { PerformLayout(); Application.DoEvents(); using var b = new Bitmap(Width, Height); DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(evidence, id + ".png")); }
        var packageFixture = Path.Combine(Path.GetTempPath(), "MapStudio-Package");
        var sourceFixture = Path.Combine(Path.GetTempPath(), "UTSL-MapStudio");
        Check("desktop_package_maps_at_root", () => AppPaths.MapsRoot(Path.Combine(packageFixture, "程序")) == Path.Combine(packageFixture, "地图"));
        Check("dev_tree_maps_at_repository_root", () => AppPaths.MapsRoot(sourceFixture) == Path.Combine(sourceFixture, "地图"));
        Check("default_output_inside_root_maps", () => Path.GetDirectoryName(output.Text) == Program.MapsRoot);
        Check("windows_GUI_without_console", () => NativeUi.GetConsoleWindow() == IntPtr.Zero);
        Check("window_visible_and_has_handle", () => Visible && Handle != IntPtr.Zero);
        Check("large_master_default_preserved", () => GetOptions() is { Width: 8400, Height: 2400, Difficulty: 2 });
        Check("eleven_styles_preserved", () => style.Items.Count == 11);
        Check("continuous_and_independent_modes_available", () => layout.Items.Count == 2);
        Check("nine_foundation_choices_preserved", () => foundationChecks.Count == 9);
        Check("seam80_and_inset40_unchanged", () => GetOptions().Overlap == 80 && GetOptions().EdgeInset == 40);
        Check("main_world_default_name", () => GetOptions().Name == "泰拉大陆");
        Check("cancel_disabled_until_owned_generation", () => !cancel.Enabled && generate.Enabled);
        Check("preview_real_estate", () => canvas.Width >= 500 && canvas.Height >= 280);
        Check("primary_button_fully_visible", () => RectangleToClient(generate.RectangleToScreen(generate.ClientRectangle)).Right <= ClientSize.Width);
        Check("inspector_is_scrollable", () => inspectorScroll.AutoScroll && inspectorScroll.DisplayRectangle.Height > inspectorScroll.Height);
        Snap("01-macos-idle");
        logsButton.PerformClick(); Check("logs_open_actual_click", () => logDrawer.Visible && previewRows.RowStyles[4].Height == 136);
        Append("UI audit: local-only; no worker, server or game connection."); Check("log_append_visible", () => log.Text.Contains("UI audit")); Snap("02-macos-logs");
        logsButton.PerformClick(); Check("logs_close_actual_click", () => !logDrawer.Visible);
        bool before = seams.Checked; seams.Checked = !before; Check("seams_switch_updates_canvas", () => canvas.ShowSeams == seams.Checked); seams.Checked = before;
        layout.SelectedIndex = 1; Check("normal_mode_options_resolver", () => GetOptions().WorldLayout == "normal-stitch"); Snap("03-macos-standard-mode"); layout.SelectedIndex = 0;
        var options = new Options { Width = 4200, Height = 1200, Output = output.Text };
        if (preview != null && File.Exists(preview))
        {
            ApplyOptions(options); LoadFinalImage(preview, options); Check("actual_saved_tile_preview_loaded", () => canvas.HasFrame && canvas.FrameSequence > 0);
            canvas.ZoomBy(1.35f); Check("zoom_control_changes_scale", () => canvas.ZoomFactor > 1); canvas.ResetView(); Check("fit_restores_panorama", () => canvas.ZoomFactor == 1);
            Snap("04-macos-real-map");
        }
        Size = new Size(1180, 760); Application.DoEvents(); Check("minimum_size_preview_visible", () => canvas.Width >= 400 && canvas.Height >= 200); Snap("05-macos-minimum-size");
        Size = new Size(1480, 980); Application.DoEvents();
        inspectorScroll.ScrollControlIntoView(output); Check("output_field_reachable_by_scrolling", () => inspectorScroll.RectangleToScreen(inspectorScroll.ClientRectangle).IntersectsWith(output.RectangleToScreen(output.ClientRectangle))); Snap("06-macos-files-inspector");
        Files.Write(Path.Combine(evidence, "result.json"), new { passed, failed, checks, dll = Files.Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.dll")),
            mapsRoot = Program.MapsRoot, runtimeRoot = Program.ToolRoot, consoleWindow = NativeUi.GetConsoleWindow().ToInt64(), productionTouched = false, gameE2E = "UNVERIFIED", at = DateTimeOffset.Now });
    }
}
