using ThreeWorlds.Live;

namespace ThreeWorlds;

internal sealed partial class MainForm
{
    string? previewDirectory;
    void UI(Action a) { if (IsDisposed || Disposing) return; if (InvokeRequired) { try { BeginInvoke(a); } catch (InvalidOperationException) { } } else a(); }
    void Append(string message) => UI(() =>
    {
        if (log.TextLength > 100_000) log.Text = log.Text[^60_000..];
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    });
    async Task PollPreview()
    {
        if (polling || active == null || IsDisposed || pipelineProgress >= 1) return;
        polling = true;
        try
        {
            string dir = previewDirectory ?? Path.Combine(active.Output, "native-source"); long lastSequence = canvas.FrameSequence;
            var data = await Task.Run(() =>
            {
                if (!File.Exists(Path.Combine(dir, "live-status.json"))) return (status: (LiveStatus?)null, frame: (LiveFrame?)null);
                var s = LiveProtocol.ReadStatus(Path.Combine(dir, "live-status.json")); LiveFrame? f = null;
                if (s != null && s.Sequence != lastSequence && File.Exists(Path.Combine(dir, "live-frame.bin"))) f = LiveProtocol.ReadFrame(Path.Combine(dir, "live-frame.bin"));
                return (status: s, frame: f);
            });
            if (IsDisposed) return;
            if (data.frame != null && data.frame.Sequence != canvas.FrameSequence) { canvas.SetFrame(data.frame); previews++; }
            if (data.status is { } s)
            {
                nativeBar.Value = Math.Clamp((int)(s.NativeProgress * 1000), 0, 1000);
                stageLabel.Text = $"原生 {s.NativeProgress:P1} · 当前阶段 {s.PassProgress:P0} · {elapsed.Elapsed:hh\\:mm\\:ss}";
                if (pipelineProgress < .72) status.Text = s.Pass + "  /  " + s.Message;
                string age = (DateTimeOffset.Now - s.At).TotalSeconds > 5 ? "（采样停留，正在执行耗时阶段）" : "";
                frameInfo.Text = $"实际图格色块预览（非客户端贴图） · 帧 {s.Frames} · {s.FrameWidth}×{s.FrameHeight} · 最近采样 {s.SampleMilliseconds:F1}ms {age}";
                if (test != null && previews is 2 or 5 or 10) Screenshot($"live-{previews:D2}");
            }
        }
        catch (IOException) { }
        catch (System.Text.Json.JsonException) { }
        finally { polling = false; }
    }
    async Task Start()
    {
        bool success = false; string? error = null;
        try
        {
            var o = GetOptions(); o.Validate(); generate.Enabled = false; cancel.Enabled = true; inspectorScroll.Enabled = false;
            foreach (var c in (Control[])[name, seed, size, difficulty, style, layout, source, output, refresh, live, foundationMode, ..foundationChecks.Values]) c.Enabled = false;
            active = o; pipelineProgress = 0; previews = 0; canvas.ClearFrame(); canvas.Configure(o); nativeBar.Value = 0;
            cancellation = new CancellationTokenSource(); var ct = cancellation.Token; elapsed.Restart();
            previewDirectory = null;
            var pipeline = new Pipeline(Append) { Stage = (text, p) => UI(() => { pipelineProgress = p; status.Text = text; }),
                PreviewSource = (path, mother) => UI(() => { previewDirectory = path; canvas.ClearFrame(); canvas.ShowSeams = mother && seams.Checked; }) };
            await Task.Run(() => pipeline.RunAsync(o, ct)); await PollPreview();
            var finalImage = Path.Combine(o.Output, "preview", "三联世界总览.png"); canvas.ShowSeams = seams.Checked; if (File.Exists(finalImage)) LoadFinalImage(finalImage, o);
            success = true; status.Text = "完成 · 三张世界和接缝已验证，未部署生产"; nativeBar.Value = 1000;
            stageLabel.Text = $"完成 · 用时 {elapsed.Elapsed:hh\\:mm\\:ss}";
            frameInfo.Text = "已附带三联世界长图 · 三张.wld与接缝校验已完成；真实在线换乘/渲染未验证。"; RefreshMapCount();
        }
        catch (OperationCanceledException) { error = "CANCELLED"; Append("已取消；不完整输出保留供诊断，不能部署。"); status.Text = "已取消 · 不完整输出不可部署"; }
        catch (Exception ex) { error = ex.ToString(); Append(ex.ToString()); status.Text = "生成失败 · 查看下方日志，失败输出不可部署"; }
        finally
        {
            elapsed.Stop(); cancellation?.Dispose(); cancellation = null; generate.Enabled = true; cancel.Enabled = false; inspectorScroll.Enabled = true;
            foreach (var c in (Control[])[name, seed, size, difficulty, style, layout, source, output, refresh, live, foundationMode, ..foundationChecks.Values]) c.Enabled = true;
            if (test != null)
            {
                Screenshot(success ? "99-complete" : "99-failed"); Directory.CreateDirectory(test.Evidence);
                Files.Write(Path.Combine(test.Evidence, "ui-test-result.json"), new { success, error, actualPreviewFramesDisplayed = previews,
                    windowVisible = Visible, windowHandle = Handle.ToInt64(), pid = Environment.ProcessId, width = Width, height = Height,
                    consoleWindow = NativeUi.GetConsoleWindow().ToInt64(), workerConsoleRequested = false, productionTouched = false,
                    test.Options, elapsedSeconds = elapsed.Elapsed.TotalSeconds, at = DateTimeOffset.Now });
            }
        }
    }
    void LoadFinalImage(string path, Options o)
    {
        using var original = new Bitmap(path); int cropTop = 42, h = original.Height - cropTop, w = original.Width;
        var pixels = new int[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) pixels[y * w + x] = original.GetPixel(x, y + cropTop).ToArgb();
        canvas.SetFrame(new LiveFrame(canvas.FrameSequence + 1, o.SourceWidth, o.Height, w, h, pixels)); canvas.ResetView();
    }
    void Screenshot(string id)
    {
        if (test == null) return;
        if (WindowState == FormWindowState.Minimized) { WindowState = FormWindowState.Normal; PerformLayout(); }
        Directory.CreateDirectory(test.Evidence); using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.Combine(test.Evidence, id + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}

internal sealed record UiTestPlan(Options Options, string Evidence, bool CloseWhenDone = true);
internal static class NativeUi
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] internal static extern IntPtr GetConsoleWindow();
}
