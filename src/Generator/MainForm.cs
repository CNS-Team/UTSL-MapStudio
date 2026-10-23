using System.Diagnostics;
using ThreeWorlds.Live;
namespace ThreeWorlds;
internal sealed partial class MainForm : Form
{
    readonly TextBox name = new() { Text = "泰拉大陆" }, seed = new() { Text = "20261023" }, source = new(), output = new();
    readonly ComboBox size = Choice(), difficulty = Choice(), style = Choice(), refresh = Choice(), foundationMode = Choice(), layout = Choice();
    readonly Dictionary<string, CheckBox> foundationChecks = [];
    readonly Label plan = Copy(), styleHint = Copy(), status = Copy("就绪 · 本地离线生成"), stageLabel = Copy("原生进度 —"), position = Copy("滚轮缩放 · 拖动平移 · 双击全景"), frameInfo = Copy("等待真实图格采样"), mapCount = Copy();
    readonly MacProgress nativeBar = new() { Dock = DockStyle.Fill, Height = 6 };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    readonly MacButton generate = new() { Text = "开始生成  →", Primary = true, Width = 148, Height = 40 }, cancel = new() { Text = "取消生成", Enabled = false, Width = 92, Height = 40 };
    readonly MacToggle live = new() { Text = "实时预览", Checked = true }, seams = new() { Text = "接缝标记", Checked = true };
    readonly MapCanvas canvas = new() { Dock = DockStyle.Fill };
    readonly Panel logDrawer = new() { Dock = DockStyle.Fill, Padding = new Padding(4, 10, 4, 0), BackColor = Color.White };
    readonly TableLayoutPanel previewRows = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, BackColor = Color.White };
    readonly MacButton logsButton = new() { Text = "运行日志", Width = 80, Height = 30 };
    readonly Panel inspectorScroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
    readonly System.Windows.Forms.Timer poll = new() { Interval = 350 };
    readonly Stopwatch elapsed = new();
    CancellationTokenSource? cancellation; Options? active; bool polling; double pipelineProgress; int previews; readonly UiTestPlan? test;
    static ComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    static Label Copy(string text = "") => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = MacTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    static Label Title(string text, float size = 10) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = MacTheme.Ink, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", size, FontStyle.Bold), Margin = Padding.Empty };
    public MainForm(UiTestPlan? test = null)
    {
        this.test = test; Text = "UTSL 地图工作室 · macOS 风格 1.4"; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9); ForeColor = MacTheme.Ink; BackColor = MacTheme.Background; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(1180, 760); var area = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(1480, area.Width - 40), Math.Min(980, area.Height - 40)); MaximizedBounds = area; DoubleBuffered = true;
        size.Items.AddRange(["大 · 8400 × 2400", "中 · 6400 × 1500", "小 · 4200 × 1200"]); size.SelectedIndex = 0;
        difficulty.Items.AddRange(["普通", "专家", "大师", "旅行"]); difficulty.SelectedIndex = 2;
        foreach (var s in StyleCatalog.All) style.Items.Add(s.Name); style.SelectedIndex = 0;
        layout.Items.AddRange(["连续母图 · 整体切片", "独立标准三世界 · 同海平面"]); layout.SelectedIndex = 0;
        refresh.Items.AddRange(["0.5 秒", "1 秒（推荐）", "2 秒", "5 秒"]); refresh.SelectedIndex = 1;
        foundationMode.Items.AddRange(["空岛自动补全", "自定义补全", "保持纯原生"]); foundationMode.SelectedIndex = 0; output.Text = AppPaths.NewMapFolder(Program.ToolRoot);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(1) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var chrome = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(244, 245, 247) };
        var traffic = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 110, Padding = new Padding(18, 11, 0, 0), BackColor = chrome.BackColor, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) { int action = i; var b = new TrafficButton(i) { Margin = Padding.Empty }; b.Click += (_, _) => { if (action == 0) Close(); else if (action == 1) WindowState = FormWindowState.Minimized; else ToggleMaximize(); }; traffic.Controls.Add(b); }
        var caption = Title("UTSL  地图工作室"); caption.TextAlign = ContentAlignment.MiddleCenter;
        var offline = Copy("●  LOCAL  /  离线工作区"); offline.TextAlign = ContentAlignment.MiddleRight; offline.Dock = DockStyle.Right; offline.Width = 205; offline.Padding = new Padding(0, 0, 22, 0); offline.ForeColor = Color.FromArgb(64, 135, 99);
        chrome.Controls.Add(caption); chrome.Controls.Add(offline); chrome.Controls.Add(traffic);
        chrome.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) MacWindow.Drag(this); }; caption.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) MacWindow.Drag(this); }; caption.DoubleClick += (_, _) => ToggleMaximize();
        shell.Controls.Add(chrome, 0, 0); shell.SetColumnSpan(chrome, 2);
        var sidebar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = MacTheme.Sidebar, Padding = new Padding(14, 22, 14, 14) };
        var nav = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, BackColor = MacTheme.Sidebar, Margin = Padding.Empty };
        AddRow(nav, Title("◈", 34), 54); AddRow(nav, Title("地图工作室", 15), 32); AddRow(nav, Copy("三联世界 · 实时预览"), 26); AddRow(nav, Copy("工作区"), 42);
        var studio = new MacButton { Text = "▧   世界生成", Selected = true, Quiet = true, Dock = DockStyle.Fill, BackColor = MacTheme.Sidebar };
        var maps = new MacButton { Text = "▱   地图文件夹", Quiet = true, Dock = DockStyle.Fill, BackColor = MacTheme.Sidebar };
        var logs = new MacButton { Text = "≡   运行日志", Quiet = true, Dock = DockStyle.Fill, BackColor = MacTheme.Sidebar };
        studio.Click += (_, _) => canvas.Focus(); maps.Click += (_, _) => OpenMaps(); logs.Click += (_, _) => ToggleLog();
        AddRow(nav, studio, 44); AddRow(nav, maps, 44); AddRow(nav, logs, 44); AddRow(nav, mapCount, 34);
        var privacy = Copy("仅在本机生成\n不连接生产服务器\n\nMAP STUDIO  1.4"); privacy.Dock = DockStyle.Bottom; privacy.Height = 102; privacy.Font = new Font(Font.FontFamily, 8); sidebar.Controls.Add(nav); sidebar.Controls.Add(privacy); shell.Controls.Add(sidebar, 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(26, 16, 26, 18), Margin = Padding.Empty };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(Title("构建你的下一片世界", 22), 0, 0); heading.Controls.Add(Copy("真实图格实时呈现。三张地图，一段完整旅程。"), 0, 1);
        var topology = new MacCard { Dock = DockStyle.Fill, Fill = Color.FromArgb(237, 243, 253), Padding = new Padding(12, 3, 12, 3), Margin = new Padding(0, 7, 0, 10), Radius = 9 };
        var route = Title("1 西   ↔   2 中   ↔   3 东", 9); route.TextAlign = ContentAlignment.MiddleCenter; topology.Controls.Add(route); heading.Controls.Add(topology, 1, 0); heading.SetRowSpan(topology, 2); body.Controls.Add(heading, 0, 0);
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 318)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var inspector = new MacCard { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = new Padding(0, 0, 18, 0) };
        var settings = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0, AutoSize = true, Padding = new Padding(0, 0, 9, 12), BackColor = Color.White, Margin = Padding.Empty }; settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(settings, Title("世界设置", 11), 32); AddField(settings, "中间世界名称", name); AddField(settings, "生成种子 / 特殊种子码", seed); AddField(settings, "三图生成方式", layout); AddField(settings, "地图样式", style);
        AddRow(settings, Pair("每片尺寸", size, "世界难度", difficulty), 66); AddRow(settings, styleHint, 64); AddRow(settings, plan, 74); AddRow(settings, Title("基础环境", 10), 30); AddField(settings, "缺失群系与建筑", foundationMode);
        var features = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty, BackColor = Color.White }; for (int i = 0; i < 3; i++) features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); int fi = 0;
        foreach (var feature in FoundationCatalog.All) { var box = new CheckBox { Text = feature.Name, Checked = new FoundationOptions().Features.Contains(feature.Id), Dock = DockStyle.Fill, AutoSize = false, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 8), UseVisualStyleBackColor = true }; foundationChecks[feature.Id] = box; features.Controls.Add(box, fi % 3, fi / 3); fi++; } AddRow(settings, features, 78);
        var pickSource = new MacButton { Text = "…", Width = 32 }; pickSource.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Terraria 世界|*.wld", Title = "选择连续母图（留空则原生生成）" }; if (d.ShowDialog(this) == DialogResult.OK) source.Text = d.FileName; };
        var pickOutput = new MacButton { Text = "…", Width = 32 }; pickOutput.Click += (_, _) => { using var d = new FolderBrowserDialog { Description = "选择新的/空的输出文件夹", InitialDirectory = Program.MapsRoot }; if (d.ShowDialog(this) == DialogResult.OK) output.Text = d.SelectedPath; };
        AddRow(settings, Title("文件与预览", 10), 34); AddField(settings, "导入母图（可选）", source, pickSource); AddField(settings, "输出到根目录 / 地图", output, pickOutput); AddField(settings, "预览刷新间隔", refresh);
        var hint = Copy("成功后附带三联世界长图。\n补全不覆盖已有对象；游戏体验未验证。"); hint.Font = new Font(Font.FontFamily, 8); AddRow(settings, hint, 51); inspectorScroll.Controls.Add(settings); inspector.Controls.Add(inspectorScroll); workspace.Controls.Add(inspector, 0, 0);
        var preview = new MacCard { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty }; previewRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); previewRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); previewRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 103)); previewRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        var previewTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty }; previewTools.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); previewTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194)); previewTools.Controls.Add(Title("实时地图预览", 11), 0, 0);
        var viewTools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, BackColor = Color.White };
        var fit = new MacButton { Text = "适应窗口", Width = 78, Height = 29 }; var plus = new MacButton { Text = "+", Width = 30, Height = 29 }; var minus = new MacButton { Text = "−", Width = 30, Height = 29 };
        fit.Click += (_, _) => canvas.ResetView(); plus.Click += (_, _) => canvas.ZoomBy(1.35f); minus.Click += (_, _) => canvas.ZoomBy(1 / 1.35f); foreach (var c in new[] { fit, plus, minus }) { c.Margin = new Padding(5, 0, 0, 0); viewTools.Controls.Add(c); } previewTools.Controls.Add(viewTools, 1, 0); previewRows.Controls.Add(previewTools, 0, 0); previewRows.Controls.Add(canvas, 0, 1); position.Font = new Font(Font.FontFamily, 8); previewRows.Controls.Add(position, 0, 2);
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) }; progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 9)); progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); progress.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); status.ForeColor = MacTheme.Accent; stageLabel.Font = frameInfo.Font = new Font(Font.FontFamily, 8); progress.Controls.Add(status, 0, 0); progress.Controls.Add(nativeBar, 0, 1); progress.Controls.Add(stageLabel, 0, 2); progress.Controls.Add(frameInfo, 0, 3); previewRows.Controls.Add(progress, 0, 3);
        log.Font = new Font("Consolas", 8.5f); log.ForeColor = Color.FromArgb(89, 98, 115); log.BackColor = Color.FromArgb(248, 249, 251); logDrawer.Controls.Add(log); logDrawer.Visible = false; previewRows.Controls.Add(logDrawer, 0, 4); preview.Controls.Add(previewRows); workspace.Controls.Add(preview, 1, 0); body.Controls.Add(workspace, 0, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 16, 0, 0) }; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty }; live.Width = 112; seams.Width = 112; live.Margin = seams.Margin = new Padding(0, 6, 4, 0); logsButton.Margin = new Padding(4, 4, 0, 0); logsButton.Click += (_, _) => ToggleLog(); tools.Controls.AddRange([live, seams, logsButton]); footer.Controls.Add(tools, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty }; cancel.Margin = new Padding(0, 0, 10, 0); actions.Controls.AddRange([generate, cancel]); footer.Controls.Add(actions, 1, 0); body.Controls.Add(footer, 0, 2); shell.Controls.Add(body, 1, 1); Controls.Add(shell);
        foreach (var c in new[] { size, style, layout }) c.SelectedIndexChanged += (_, _) => RefreshPlan(); name.TextChanged += (_, _) => RefreshPlan(); source.TextChanged += (_, _) => RefreshPlan(); RefreshPlan(); RefreshMapCount();
        generate.Click += async (_, _) => await Start(); cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; Append("请求取消；只终止本窗口拥有的本地worker。"); }; seams.CheckedChanged += (_, _) => { canvas.ShowSeams = seams.Checked; canvas.Invalidate(); }; canvas.PositionChanged = text => position.Text = text;
        poll.Tick += async (_, _) => await PollPreview(); poll.Start(); FormClosing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); Append("正在取消本地生成，请等待worker退出后关闭。"); } }; FormClosed += (_, _) => poll.Dispose(); Shown += (_, _) => MacWindow.Round(this);
        if (test != null) Shown += async (_, _) => { ApplyOptions(test.Options); await Task.Delay(400); Screenshot("01-window-open"); await Start(); if (test.CloseWhenDone) Close(); };
    }
    static void AddRow(TableLayoutPanel panel, Control control, int height) { int row = panel.RowCount; panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Dock = DockStyle.Fill; panel.Controls.Add(control, 0, row); }
    static Control Input(Control input, Control? button = null)
    {
        var frame = new MacCard { Radius = 7, Fill = MacTheme.Input, Dock = DockStyle.Fill, Padding = new Padding(10, 7, 7, 3), Margin = new Padding(0, 0, 0, 1) }; input.BackColor = MacTheme.Input; input.ForeColor = MacTheme.Ink; input.Dock = DockStyle.Fill; input.Font = new Font("Microsoft YaHei UI", 9); if (input is TextBox t) t.BorderStyle = BorderStyle.None;
        frame.Controls.Add(input); if (button != null) { button.Dock = DockStyle.Right; button.Width = 28; button.Margin = Padding.Empty; frame.Controls.Add(button); } return frame;
    }
    static void AddField(TableLayoutPanel settings, string title, Control input, Control? button = null)
    {
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 2, 0, 6) }; field.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); field.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); var label = Copy(title); label.Font = new Font("Microsoft YaHei UI", 8); field.Controls.Add(label, 0, 0); field.Controls.Add(Input(input, button), 0, 1); AddRow(settings, field, 69);
    }
    static Control Pair(string title1, Control left, string title2, Control right)
    {
        var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 2, 0, 6) }; pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37)); pair.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); var l = Copy(title1); var r = Copy(title2); l.Font = r.Font = new Font("Microsoft YaHei UI", 8); pair.Controls.Add(l, 0, 0); pair.Controls.Add(r, 1, 0); var lf = Input(left); lf.Margin = new Padding(0, 0, 8, 1); pair.Controls.Add(lf, 0, 1); pair.Controls.Add(Input(right), 1, 1); return pair;
    }
    void ToggleLog() { logDrawer.Visible = !logDrawer.Visible; previewRows.RowStyles[4].Height = logDrawer.Visible ? 136 : 0; logsButton.Selected = logDrawer.Visible; logsButton.Invalidate(); }
    void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    void OpenMaps() { Directory.CreateDirectory(Program.MapsRoot); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Program.MapsRoot }, UseShellExecute = true }); }
    void RefreshMapCount() { mapCount.Text = Directory.Exists(Program.MapsRoot) ? $"{Directory.EnumerateDirectories(Program.MapsRoot).Count()} 个地图项目" : "地图将在这里汇集"; }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m); if (m.Msg != 0x84 || WindowState != FormWindowState.Normal || (int)m.Result != 1) return;
        var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)))); int edge = Math.Max(6, DeviceDpi / 16); bool left = point.X < edge, right = point.X >= Width - edge, top = point.Y < edge, bottom = point.Y >= Height - edge;
        m.Result = (IntPtr)(top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 1);
    }
    Options GetOptions()
    {
        var dims = size.SelectedIndex == 0 ? (8400, 2400) : size.SelectedIndex == 1 ? (6400, 1500) : (4200, 1200);
        return new Options { Name = name.Text.Trim(), Seed = seed.Text.Trim(), Width = dims.Item1, Height = dims.Item2, WorldLayout = layout.SelectedIndex == 1 ? "normal-stitch" : "continuous", Difficulty = difficulty.SelectedIndex, Style = StyleCatalog.All[Math.Max(0, style.SelectedIndex)].Id, LivePreview = live.Checked, Foundations = new FoundationOptions { Mode = new[] { "skyblock-auto", "enabled", "disabled" }[foundationMode.SelectedIndex], Features = foundationChecks.Where(p => p.Value.Checked).Select(p => p.Key).ToArray() }, PreviewIntervalMs = new[] { 500, 1000, 2000, 5000 }[refresh.SelectedIndex], Source = string.IsNullOrWhiteSpace(source.Text) ? null : source.Text.Trim(), Output = output.Text.Trim() };
    }
    void ApplyOptions(Options o)
    {
        name.Text = o.Name; seed.Text = o.Seed; size.SelectedIndex = o.Width == 8400 ? 0 : o.Width == 6400 ? 1 : 2; layout.SelectedIndex = o.WorldLayout == "normal-stitch" ? 1 : 0; difficulty.SelectedIndex = o.Difficulty; style.SelectedIndex = Array.FindIndex(StyleCatalog.All, s => s.Id == o.Style); source.Text = o.Source ?? ""; output.Text = o.Output; live.Checked = o.LivePreview; foundationMode.SelectedIndex = Array.IndexOf(new[] { "skyblock-auto", "enabled", "disabled" }, o.Foundations.Mode); foreach (var f in foundationChecks) f.Value.Checked = o.Foundations.Features.Contains(f.Key); refresh.SelectedIndex = Array.IndexOf(new[] { 500, 1000, 2000, 5000 }, o.PreviewIntervalMs); if (refresh.SelectedIndex < 0) refresh.SelectedIndex = 1;
    }
    void RefreshPlan()
    {
        var o = GetOptions(); styleHint.Text = o.WorldLayout == "normal-stitch" ? "每张独立原版地图；三图海平面相同。接缝带相同Y，内陆生态保留。" : source.Text.Length > 0 ? "导入连续母图，保留原地形与种子标志。" : StyleCatalog.Get(o.Style).Description;
        plan.Text = $"西 — {o.Name}（默认）— 东\n{o.Width:N0} × {o.Height:N0} / 片 · 重叠 80 格\n同Y穿越 · 成功附带拼接长图"; if (cancellation == null) canvas.Configure(o);
    }
}
