using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ThreeWorlds;

internal static class MacTheme
{
    internal static readonly Color Background = Color.FromArgb(246, 247, 249), Sidebar = Color.FromArgb(237, 239, 243),
        Ink = Color.FromArgb(35, 40, 51), Muted = Color.FromArgb(120, 127, 141), Accent = Color.FromArgb(0, 112, 245),
        Line = Color.FromArgb(226, 229, 235), Input = Color.FromArgb(246, 247, 250);
    internal static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}

internal class MacCard : Panel
{
    [DefaultValue(14)] public int Radius { get; set; } = 14;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Fill { get; set; } = Color.White;
    public MacCard() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = MacTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Radius);
        using var b = new SolidBrush(Fill); using var pen = new Pen(MacTheme.Line);
        e.Graphics.FillPath(b, p); e.Graphics.DrawPath(pen, p);
    }
}

internal sealed class MacButton : Button
{
    bool hover;
    [DefaultValue(false)] public bool Primary { get; set; }
    [DefaultValue(false)] public bool Selected { get; set; }
    [DefaultValue(false)] public bool Quiet { get; set; }
    public MacButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Size = new(108, 36); Cursor = Cursors.Hand;
        Margin = new Padding(0); UseVisualStyleBackColor = false; Font = new Font("Microsoft YaHei UI", 9);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = !Enabled ? Color.FromArgb(238, 240, 244) : Primary ? (hover ? Color.FromArgb(0, 98, 222) : MacTheme.Accent) :
            Selected ? Color.FromArgb(220, 232, 252) : hover ? Color.FromArgb(229, 234, 242) : Quiet ? BackColor : Color.White;
        using var p = MacTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var b = new SolidBrush(fill); e.Graphics.FillPath(b, p);
        if (!Quiet && !Primary) { using var pen = new Pen(MacTheme.Line); e.Graphics.DrawPath(pen, p); }
        var ink = !Enabled ? Color.FromArgb(162, 168, 179) : Primary ? Color.White : Selected ? MacTheme.Accent : MacTheme.Ink;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) { using var pen = new Pen(MacTheme.Accent); e.Graphics.DrawPath(pen, p); }
    }
}

internal sealed class MacToggle : CheckBox
{
    public MacToggle() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AutoSize = false; Size = new(124, 28); Cursor = Cursors.Hand; }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = MacTheme.Round(new RectangleF(1, (Height - 18) / 2f, 32, 18), 9);
        using var b = new SolidBrush(Enabled && Checked ? MacTheme.Accent : Color.FromArgb(196, 201, 211)); e.Graphics.FillPath(b, track);
        e.Graphics.FillEllipse(Brushes.White, Checked ? 16 : 3, (Height - 14) / 2f, 14, 14);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(41, 0, Width - 41, Height), Enabled ? MacTheme.Ink : MacTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class MacProgress : Control
{
    int value;
    [DefaultValue(0)] public int Minimum { get; set; }
    [DefaultValue(1000)] public int Maximum { get; set; } = 1000;
    [DefaultValue(0)] public int Value { get => value; set { this.value = Math.Clamp(value, Minimum, Maximum); Invalidate(); } }
    public MacProgress() { DoubleBuffered = true; Height = 6; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = MacTheme.Round(new RectangleF(0, 0, Width, Height), 3);
        using var b = new SolidBrush(Color.FromArgb(230, 234, 241)); e.Graphics.FillPath(b, track);
        float width = Width * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
        if (width > 0) { using var p = MacTheme.Round(new RectangleF(0, 0, Math.Max(6, width), Height), 3); using var blue = new SolidBrush(MacTheme.Accent); e.Graphics.FillPath(blue, p); }
    }
}

internal sealed class TrafficButton : Button
{
    readonly int action;
    bool hover;
    public TrafficButton(int action)
    {
        this.action = action; Size = new(22, 28); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = action == 0 ? "关闭窗口" : action == 1 ? "最小化窗口" : "最大化或还原窗口"; Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(action == 0 ? Color.FromArgb(255, 95, 87) : action == 1 ? Color.FromArgb(254, 188, 46) : Color.FromArgb(40, 200, 64));
        e.Graphics.FillEllipse(b, 4, 7, 13, 13);
        if (!hover) return; using var pen = new Pen(Color.FromArgb(110, 65, 47), 1.2f);
        if (action == 0) { e.Graphics.DrawLine(pen, 8, 11, 13, 16); e.Graphics.DrawLine(pen, 13, 11, 8, 16); }
        else { e.Graphics.DrawLine(pen, 8, 13, 13, 13); if (action == 2) e.Graphics.DrawLine(pen, 10.5f, 10.5f, 10.5f, 15.5f); }
    }
}

internal static class MacWindow
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    internal static void Drag(Form form) { ReleaseCapture(); SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
    internal static void Round(Form form) { try { int v = 2; DwmSetWindowAttribute(form.Handle, 33, ref v, sizeof(int)); } catch (DllNotFoundException) { } }
}
