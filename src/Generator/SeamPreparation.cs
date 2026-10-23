using System.Drawing;
using TEdit.Terraria;

namespace ThreeWorlds;

public sealed record PreparedSeam(int GlobalX, int HalfWidth, long ChangedTiles, string Before, string After, long CheckedLandingBodies,
    long ObstructedLandingBodies, int[] ProtectedObjectRows);
internal static class SeamPreparation
{
    // A short, full-depth identical profile ensures a 20x42px player can land
    // 1-2 tiles inside the opposite boundary at EXACT Y (including cave heights).
    // This is an owned offline mother world, not online player-building removal.
    static readonly HashSet<ushort> NaturalPlants = [3, 5, 24, 32, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 201, 205, 227, 233, 323];
    public static PreparedSeam[] Prepare(World w, Options o, int offset)
    {
        var result = new List<PreparedSeam>();
        foreach (int seam in new[] { offset + o.Width - 40, offset + o.Stride + o.Width - 40 })
        {
            const int half = 4;
            var area = new Rectangle(seam - half, 0, half * 2, w.TilesHigh);
            var protectedRows = new HashSet<int>();
            void Protect(int top, int bottom)
            { for (int y = Math.Max(0, top - 4); y <= Math.Min(w.TilesHigh - 1, bottom + 4); y++) protectedRows.Add(y); }
            foreach (var c in w.Chests.Where(c => area.IntersectsWith(new(c.X, c.Y, 2, 2)))) Protect(c.Y, c.Y + 2);
            foreach (var s in w.Signs.Where(s => area.IntersectsWith(new(s.X, s.Y, 2, 2)))) Protect(s.Y, s.Y + 2);
            foreach (var e in w.TileEntities.Where(e => area.Contains(e.PosX, e.PosY))) Protect(e.PosY - 3, e.PosY + 5);
            foreach (var p in w.PressurePlates.Where(p => area.Contains(p.PosX, p.PosY))) Protect(p.PosY, p.PosY);
            // Native structures intersecting the seam are retained IN PLACE. Their
            // surrounding rows are excluded from extrusion, not deleted/duplicated.
            for (int x = area.Left; x < area.Right; x++) for (int y = 0; y < w.TilesHigh; y++)
            {
                var t = w.Tiles[x, y];
                if (t.IsActive && !NaturalPlants.Contains(t.Type) && (t.IsChest() || t.IsSign() || t.IsTileEntity() ||
                    t.Type < w.TileFrameImportant.Length && w.TileFrameImportant[t.Type]))
                    Protect(y, y);
            }
            string before = FoundationWorlds.RegionDigest(w, area); long changed = 0;
            for (int y = 0; y < w.TilesHigh; y++)
            {
                if (protectedRows.Contains(y)) continue;
                Tile profile = w.Tiles[seam, y];
                if (profile.IsActive && NaturalPlants.Contains(profile.Type)) { profile.IsActive = false; profile.Type = 0; profile.U = profile.V = 0; }
                if (profile.IsActive) profile.BrickStyle = BrickStyle.Full;
                for (int x = area.Left; x < area.Right; x++) if (!w.Tiles[x, y].Equals(profile)) { w.Tiles[x, y] = profile; changed++; }
            }
            // Test every integer-pixel standing height. Column profiles are equal,
            // hence the same implication also holds at fractional/subpixel Y.
            var bodies = Verify(w, seam, protectedRows);
            result.Add(new(seam, half, changed, before, FoundationWorlds.RegionDigest(w, area), bodies.Checked, bodies.Blocked, protectedRows.Order().ToArray()));
        }
        return result.ToArray();
    }
    internal static bool Solid(World w, int x, int y)
    {
        var t = w.Tiles[x, y];
        return t.IsActive && !t.InActive && TerrariaDataStore.Instance.TileById.TryGetValue(t.Type, out var p) && p.IsSolid && !p.IsPlatform;
    }
    internal static (long Checked, long Blocked) Verify(World w, int seam, ISet<int>? protectedRows = null)
    {
        bool Clear(int xPx, int yPx)
        {
            for (int x = xPx / 16; x <= (xPx + 19) / 16; x++) for (int y = yPx / 16; y <= (yPx + 41) / 16; y++) if (Solid(w, x, y)) return false;
            return true;
        }
        long checkedBodies = 0, blockedBodies = 0;
        for (int y = 32; y < (w.TilesHigh - 2) * 16 - 42; y++) foreach (bool east in new[] { true, false })
        {
            int sourceX = east ? seam * 16 - 20 : seam * 16;
            if (!Clear(sourceX, y)) continue;
            foreach (int inset in new[] { 1, 2 })
            {
                int targetX = east ? (seam + inset) * 16 : (seam - inset) * 16 - 20;
                if (!Clear(targetX, y))
                {
                    bool objectBand = protectedRows != null && Enumerable.Range(y / 16, (y + 41) / 16 - y / 16 + 1).Any(protectedRows.Contains);
                    if (!objectBand) throw new InvalidDataException($"接缝 x={seam} 自然同Y通道不连续：yPx={y},east={east},inset={inset}");
                    blockedBodies++; // explicit clipping allowance, not a false clear-PASS
                }
                checkedBodies++;
            }
        }
        return (checkedBodies, blockedBodies);
    }
}
