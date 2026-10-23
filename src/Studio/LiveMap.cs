using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ThreeWorlds.Live;
namespace MapStudio;
public sealed class LiveMap : Control
{
    Bitmap? image; double zoom = 1; Vector pan; Point? drag; int worldWidth, worldHeight, sliceWidth = 8400; string worldName = "泰拉大陆";
    public long Sequence { get; private set; }
    public bool HasFrame => image != null;
    public bool ShowSeams { get; set; } = true;
    public double ZoomFactor => zoom;
    public Action<string>? PositionChanged { get; set; }
    public LiveMap() { ClipToBounds = true; Focusable = true; DoubleTapped += (_, _) => Fit(); }
    public void Configure(StudioOptions o) { sliceWidth = o.Width; worldName = o.Name; InvalidateVisual(); }
    public void Clear() { image?.Dispose(); image = null; Sequence = 0; Fit(); }
    public void Fit() { zoom = 1; pan = default; InvalidateVisual(); }
    public void Zoom(double factor) { if (image == null) return; zoom = Math.Clamp(zoom * factor, 1, 24); if (zoom == 1) pan = default; InvalidateVisual(); }
    public void SetFrame(LiveFrame f)
    {
        var next = new WriteableBitmap(new PixelSize(f.Width, f.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = next.Lock()) for (int y = 0; y < f.Height; y++) Marshal.Copy(f.Pixels, y * f.Width, fb.Address + y * fb.RowBytes, f.Width);
        var old = image; image = next; old?.Dispose(); worldWidth = f.WorldWidth; worldHeight = f.WorldHeight; Sequence = f.Sequence; InvalidateVisual();
    }
    public void LoadPanorama(string path, StudioOptions o)
    { var next = new Bitmap(path); var old = image; image = next; old?.Dispose(); worldWidth = o.SourceWidth; worldHeight = o.Height; Configure(o); Sequence++; Fit(); }
    Rect ImageRect()
    {
        if (image == null) return default; double fit = Math.Min(Math.Max(1, Bounds.Width - 44) / image.PixelSize.Width, Math.Max(1, Bounds.Height - 74) / image.PixelSize.Height);
        double w = image.PixelSize.Width * fit * zoom, h = image.PixelSize.Height * fit * zoom;
        return new((Bounds.Width - w) / 2 + pan.X, (Bounds.Height - h) / 2 + pan.Y + 12, w, h);
    }
    static void Text(DrawingContext g, string text, Point at, double size = 12, string color = "#93A7C7", bool bold = false)
    {
        var f = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI", FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush.Parse(color));
        g.DrawText(f, at);
    }
    public override void Render(DrawingContext g)
    {
        base.Render(g); g.FillRectangle(Brush.Parse("#121A26"), new Rect(Bounds.Size));
        if (image == null)
        {
            var dot = Brush.Parse("#28364B"); for (int x = 20; x < Bounds.Width; x += 34) for (int y = 20; y < Bounds.Height; y += 34) g.DrawEllipse(dot, null, new Point(x, y), .65, .65);
            double cx = Bounds.Width / 2, cy = Bounds.Height / 2 - 52;
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(cx - 92 + i * 64, cy - (i == 1 ? 8 : 0), 56, 66);
                g.DrawRectangle(Brush.Parse(i == 1 ? "#253C61" : "#1D2C42"), new Pen(Brush.Parse("#5279AB"), 1), r, 8, 8);
                g.DrawLine(new Pen(Brush.Parse("#79A1DF")), new Point(r.X + 10, r.Y + 47), new Point(r.X + 25, r.Y + 28));
                g.DrawLine(new Pen(Brush.Parse("#79A1DF")), new Point(r.X + 25, r.Y + 28), new Point(r.X + 44, r.Y + 47));
            }
            Text(g, "世界，从这里开始", new Point(cx - 94, cy + 100), 22, "#DDE8FA", true);
            Text(g, "选择种子与样式，实时看见世界生成", new Point(cx - 132, cy + 146), 13);
            Text(g, "真实图格采样 · 非进度动画 · 上图仅为布局图标", new Point(cx - 155, cy + 178), 11, "#687E9F"); return;
        }
        var rect = ImageRect(); g.DrawImage(image, new Rect(image.Size), rect); g.DrawRectangle(null, new Pen(Brush.Parse("#3C526F")), rect);
        if (ShowSeams && worldWidth > sliceWidth)
        {
            int stride = sliceWidth - 80;
            for (int i = 0; i < 2; i++) { double x = rect.X + (i * stride + sliceWidth - 40) * rect.Width / worldWidth; g.DrawLine(new Pen(Brush.Parse("#91B6FF"), 1), new Point(x, rect.Y), new Point(x, rect.Bottom)); }
            for (int i = 0; i < 3; i++)
            {
                string title = i == 0 ? "1  " + worldName + "·西" : i == 1 ? "2  " + worldName + "（默认）" : "3  " + worldName + "·东";
                Text(g, title, new Point(rect.X + (i * stride + sliceWidth / 2d) * rect.Width / worldWidth - 55, Math.Max(8, rect.Y - 26)), 11, "#BDD3F6");
            }
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { Zoom(e.Delta.Y > 0 ? 1.3 : 1 / 1.3); e.Handled = true; }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { Focus(); if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { drag = e.GetPosition(this); e.Pointer.Capture(this); } base.OnPointerPressed(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { drag = null; e.Pointer.Capture(null); base.OnPointerReleased(e); }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this); if (drag is Point old) { pan += point - old; drag = point; InvalidateVisual(); }
        if (image != null) { var r = ImageRect(); if (r.Contains(point)) PositionChanged?.Invoke($"母图 X {(int)((point.X - r.X) * worldWidth / r.Width):N0} / Y {(int)((point.Y - r.Y) * worldHeight / r.Height):N0}  ·  缩放 {zoom:F1}×  ·  双击恢复全景"); }
        base.OnPointerMoved(e);
    }
}
