using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ThreeWorlds.Live;

namespace ThreeWorlds;

internal sealed class MapCanvas : Control
{
    Bitmap? image;
    float zoom = 1;
    PointF pan;
    Point? drag;
    int worldWidth, worldHeight, stride, edge = 40, sliceWidth;
    string worldName = "泰拉大陆";
    [System.ComponentModel.DefaultValue(true)]
    public bool ShowSeams { get; set; } = true;
    public bool HasFrame => image != null;
    public long FrameSequence { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<string>? PositionChanged { get; set; }
    public MapCanvas()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(246, 248, 252); Cursor = Cursors.Cross;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); TabStop = true;
    }
    public void Configure(Options o) { stride = o.Stride; edge = o.EdgeInset; sliceWidth = o.Width; worldName = o.Name; Invalidate(); }
    public void ResetView() { zoom = 1; pan = PointF.Empty; Invalidate(); }
    internal float ZoomFactor => zoom;
    public void ZoomBy(float factor) { if (image == null) return; zoom = Math.Clamp(zoom * factor, 1, 18); if (zoom == 1) pan = PointF.Empty; Invalidate(); }
    public void ClearFrame() { var old = image; image = null; old?.Dispose(); FrameSequence = 0; ResetView(); }
    public void SetFrame(LiveFrame frame)
    {
        if (IsDisposed) return;
        var next = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var data = next.LockBits(new Rectangle(0, 0, next.Width, next.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < next.Height; y++) Marshal.Copy(frame.Pixels, y * next.Width, data.Scan0 + y * data.Stride, next.Width); }
        finally { next.UnlockBits(data); }
        var old = image; image = next; worldWidth = frame.WorldWidth; worldHeight = frame.WorldHeight;
        FrameSequence = frame.Sequence; old?.Dispose(); Invalidate();
    }
    RectangleF ImageRect()
    {
        if (image == null) return RectangleF.Empty;
        float fit = Math.Min((ClientSize.Width - 32f) / image.Width, (ClientSize.Height - 60f) / image.Height);
        float width = image.Width * fit * zoom, height = image.Height * fit * zoom;
        return new((Width - width) / 2 + pan.X, (Height - height) / 2 + 15 + pan.Y, width, height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        if (image == null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var grid = new Pen(Color.FromArgb(233, 237, 244));
            for (int x = 22; x < Width; x += 34) for (int y = 22; y < Height; y += 34) g.FillEllipse(Brushes.LightGray, x, y, 1, 1);
            int cx = Width / 2, cy = Height / 2 - 54;
            for (int i = -1; i <= 1; i++)
            {
                var iconRect = new RectangleF(cx - 85 + (i + 1) * 57, cy - 30 + (i == 0 ? -8 : 0), 52, 66);
                using var shape = MacTheme.Round(iconRect, 8); using var b = new SolidBrush(i == 0 ? Color.FromArgb(222, 233, 253) : Color.FromArgb(232, 238, 248));
                g.FillPath(b, shape); using var pen = new Pen(Color.FromArgb(136, 167, 220), 1.4f); g.DrawPath(pen, shape);
                g.DrawLines(pen, [new PointF(iconRect.X + 8, iconRect.Y + 45), new PointF(iconRect.X + 19, iconRect.Y + 30), new PointF(iconRect.X + 28, iconRect.Y + 39), new PointF(iconRect.X + 36, iconRect.Y + 26), new PointF(iconRect.X + 44, iconRect.Y + 45)]);
            }
            using var heading = new Font(Font.FontFamily, 16, FontStyle.Bold);
            TextRenderer.DrawText(g, "世界，从这里开始", heading, new Rectangle(0, cy + 63, Width, 34), MacTheme.Ink, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "选择种子与样式，点击「开始生成」", Font, new Rectangle(0, cy + 109, Width, 28), MacTheme.Muted, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "真实图格预览 · 非进度动画 · 上图仅为布局图标", Font, new Rectangle(0, cy + 145, Width, 28), MacTheme.Muted, TextFormatFlags.HorizontalCenter);
            return;
        }
        var r = ImageRect(); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(image, r);
        using var outline = new Pen(Color.FromArgb(67, 91, 111)); g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);
        if (ShowSeams && worldWidth > 0)
        {
            using var line = new Pen(Color.FromArgb(225, 95, 216, 186)) { DashStyle = DashStyle.Dash };
            using var band = new SolidBrush(Color.FromArgb(45, 95, 216, 186));
            for (int i = 0; i < 2; i++)
            {
                float x = r.X + (i * stride + sliceWidth - edge) * r.Width / worldWidth;
                float bw = edge * 2f * r.Width / worldWidth; g.FillRectangle(band, x - bw / 2, r.Y, Math.Max(2, bw), r.Height); g.DrawLine(line, x, r.Y, x, r.Bottom);
            }
            string[] names = ["1 · " + worldName + "·西", "2 · " + worldName + "（默认）", "3 · " + worldName + "·东"];
            for (int i = 0; i < 3; i++)
            {
                float x = r.X + (i * stride + sliceWidth / 2f) * r.Width / worldWidth;
                var textSize = TextRenderer.MeasureText(names[i], Font);
                TextRenderer.DrawText(g, names[i], Font, new Point((int)x - textSize.Width / 2, Math.Max(2, (int)r.Y - 28)), MacTheme.Ink);
            }
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (image == null) return; var before = ImageRect();
        float rx = (e.X - before.X) / before.Width, ry = (e.Y - before.Y) / before.Height;
        zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.35f : 1 / 1.35f), 1, 18); var after = ImageRect();
        pan.X += e.X - (after.X + rx * after.Width); pan.Y += e.Y - (after.Y + ry * after.Height);
        if (zoom == 1) pan = PointF.Empty; Invalidate(); base.OnMouseWheel(e);
    }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { drag = e.Location; Capture = true; } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = null; Capture = false; base.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { ResetView(); base.OnMouseDoubleClick(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag is Point previous) { pan.X += e.X - previous.X; pan.Y += e.Y - previous.Y; drag = e.Location; Invalidate(); }
        if (image != null)
        {
            var r = ImageRect(); if (r.Contains(e.Location))
            {
                int gx = Math.Clamp((int)((e.X - r.X) * worldWidth / r.Width), 0, worldWidth - 1);
                int y = Math.Clamp((int)((e.Y - r.Y) * worldHeight / r.Height), 0, worldHeight - 1);
                PositionChanged?.Invoke($"母图 X {gx:N0} / Y {y:N0}   ·   缩放 {zoom:F1}×   ·   滚轮缩放 / 拖动平移 / 双击全景");
            }
        }
        base.OnMouseMove(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}
