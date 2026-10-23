using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TEdit.Terraria;

namespace ThreeWorlds;

internal static class Preview
{
    static Color Pixel(Tile t, int y, World w)
    {
        if (t.LiquidAmount > 0) return t.LiquidType switch { LiquidType.Lava => Color.OrangeRed, LiquidType.Honey => Color.Goldenrod, _ => Color.RoyalBlue };
        if (t.IsActive && TerrariaDataStore.Instance.TileById.TryGetValue(t.Type, out var prop))
            return Color.FromArgb(prop.Color.R, prop.Color.G, prop.Color.B);
        if (t.Wall > 0) return Color.FromArgb(62, 52, 50);
        return y < w.GroundLevel ? Color.FromArgb(116, 174, 218) : y < w.RockLevel ? Color.FromArgb(55, 43, 33) : Color.FromArgb(25, 25, 31);
    }
    public static void Render(string output, SliceResult[] results, Options o)
    {
        string dir = Path.Combine(output, "preview"); Directory.CreateDirectory(dir);
        const int scale = 8; int total = o.SourceWidth;
        const int longScale = 2;
        int lw = (total + longScale - 1) / longScale, lh = (o.Height + longScale - 1) / longScale;
        var longPixels = new int[lw * lh];
        using var image = new Bitmap((total + scale - 1) / scale, (o.Height + scale - 1) / scale + 42);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.FromArgb(24, 28, 36));
        for (int index = 0; index < results.Length; index++)
        {
            var w = WorldIO.Load(results[index].File.Path);
            int startGlobal = index * o.Stride, endGlobal = index == 2 ? total : (index + 1) * o.Stride;
            // Sample a single global lattice; do not stitch previews by concatenating duplicated strips.
            for (int px = (startGlobal + scale - 1) / scale; px * scale < endGlobal; px++)
                for (int py = 0; py * scale < o.Height; py++)
                    image.SetPixel(px, py + 42, Pixel(w.Tiles[px * scale - startGlobal, py * scale], py * scale, w));
            for (int px = (startGlobal + longScale - 1) / longScale; px * longScale < endGlobal; px++)
                for (int py = 0; py < lh; py++)
                    longPixels[py * lw + px] = Pixel(w.Tiles[px * longScale - startGlobal, py * longScale], py * longScale, w).ToArgb();
            using (var g = Graphics.FromImage(image))
            using (var font = new Font("Microsoft YaHei UI", 10))
            {
                g.DrawString(w.Title + (index == 1 ? "（默认出生）" : ""), font, Brushes.White, (startGlobal + o.Width / 2) / scale - 70, 10);
                int sx = (startGlobal + w.SpawnX) / scale, sy = 42 + w.SpawnY / scale;
                g.DrawEllipse(Pens.Yellow, sx - 4, sy - 4, 8, 8);
            }
            if (index < 2)
            {
                // Includes both sides of the playable seam in the same source coordinate system.
                int seam = o.Width - o.EdgeInset;
                int half = Math.Min(100, o.Width / 4);
                var neighbor = WorldIO.Load(results[index + 1].File.Path);
                using var detail = new Bitmap(half * 2, o.Height);
                for (int x = 0; x < half * 2; x++) for (int y = 0; y < o.Height; y++)
                {
                    int local = seam - half + x;
                    // Left half is the left world's actual data; right half is the right
                    // world's actual data. Never repeat a clamped last column as fake terrain.
                    detail.SetPixel(x, y, local < seam ? Pixel(w.Tiles[local, y], y, w) :
                        Pixel(neighbor.Tiles[local - o.Stride, y], y, neighbor));
                }
                detail.Save(Path.Combine(dir, "seam-" + (index + 1) + ".png"), ImageFormat.Png);
                neighbor.Tiles = null!;
            }
            w.Tiles = null!; WorldIO.Collect();
        }
        using (var g = Graphics.FromImage(image))
        {
            for (int i = 0; i < 2; i++)
            {
                int x = (i * o.Stride + o.Width - o.EdgeInset) / scale;
                g.DrawLine(Pens.Red, x, 42, x, image.Height - 1);
            }
        }
        image.Save(Path.Combine(dir, "三联世界总览.png"), ImageFormat.Png);
        // A separate high-detail, overlap-deduplicated long image; no fake upscaling.
        using var panorama = new Bitmap(lw, lh, PixelFormat.Format32bppArgb);
        var locked = panorama.LockBits(new Rectangle(0, 0, lw, lh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < lh; y++) Marshal.Copy(longPixels, y * lw, locked.Scan0 + y * locked.Stride, lw); }
        finally { panorama.UnlockBits(locked); }
        panorama.Save(Path.Combine(output, "三联世界长图.png"), ImageFormat.Png);
        Files.Write(Path.Combine(output, "长图说明.json"), new { image = Files.Inspect(Path.Combine(output, "三联世界长图.png")), width = lw, height = lh,
            tilesPerPixel = longScale, logicalWidthTiles = total, heightTiles = o.Height, o.WorldLayout,
            chain = results.Select(r => r.Spec.Name).ToArray(), duplicatedOverlapRemoved = true,
            scope = "actual saved tile-color panorama; not Terraria client textures; no runtime terrain/liquid sync", productionTouched = false });
    }
}
