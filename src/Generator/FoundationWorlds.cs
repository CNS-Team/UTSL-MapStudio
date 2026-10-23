using System.Drawing;
using TEdit.Geometry;
using TEdit.Terraria;

namespace ThreeWorlds;

public sealed record FoundationScan(string Id, string Name, long Tiles, bool Present, Rectangle? Bounds);
public sealed record FoundationPatch(string[] Features, Artifact Donor, Rectangle Source, Rectangle Destination,
    int Chests, int Signs, int TileEntities, int Npcs, string TileDigest, string Policy);

// All modifications happen on an OWNED mother-world copy, BEFORE slicing. Whole
// native structures/loot/unsafe walls are transplanted, not fake decorative boxes.
// Never copy boss/progression flags, or fabricate altar/chest loot.
internal static class FoundationWorlds
{
    public static FoundationScan[] Scan(World w, Rectangle? window = null) => FoundationCatalog.All.Select(f => Scan(w, f, window)).ToArray();
    static FoundationScan Scan(World w, FoundationFeature feature, Rectangle? window)
    {
        var area = window ?? new Rectangle(0, 0, w.TilesWide, w.TilesHigh);
        long count = 0; int left = w.TilesWide, right = -1, top = w.TilesHigh, bottom = -1;
        for (int x = area.Left; x < area.Right; x++) for (int y = area.Top; y < area.Bottom; y++)
        {
            if (!FoundationCatalog.Matches(w.Tiles[x, y], feature)) continue;
            count++; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        bool complete = count >= feature.Minimum;
        if (feature.Id == "temple") complete &= ContainsType(w, 237, area); // actual Lihzahrd altar
        return new(feature.Id, feature.Name, count, complete, count == 0 ? null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
    }
    static bool ContainsType(World w, ushort type, Rectangle area)
    {
        for (int x = area.Left; x < area.Right; x++) for (int y = area.Top; y < area.Bottom; y++)
            if (w.Tiles[x, y].IsActive && w.Tiles[x, y].Type == type) return true;
        return false;
    }
    public static string[] Missing(World w, FoundationOptions options, Rectangle? window = null) => Scan(w, window).Where(s => options.Features.Contains(s.Id) && !s.Present).Select(s => s.Id).ToArray();

    // Keep the densest natural horizontal biome cluster, rather than a huge
    // rectangle spanning unrelated patches on both sides of a donor world.
    static Rectangle Cluster(World w, FoundationFeature f)
    {
        var columns = new List<(int x, long n, int top, int bottom)>();
        for (int x = 16; x < w.TilesWide - 16; x++)
        {
            long count = 0; int top = w.TilesHigh, bottom = -1;
            for (int y = 16; y < w.TilesHigh - 16; y++) if (FoundationCatalog.Matches(w.Tiles[x, y], f)) { count++; top = Math.Min(top, y); bottom = y; }
            if (count != 0) columns.Add((x, count, top, bottom));
        }
        if (columns.Count == 0) throw new InvalidDataException("原生素材世界缺少" + f.Name);
        var groups = new List<List<(int x, long n, int top, int bottom)>>();
        foreach (var c in columns)
        {
            if (groups.Count == 0 || c.x - groups[^1][^1].x > 100) groups.Add([]);
            groups[^1].Add(c);
        }
        var group = f.Id is "dungeon" or "temple" ? columns : groups.OrderByDescending(g => g.Sum(c => c.n)).First();
        // Include generous native surrounding terrain, full entrances and objects.
        int padding = f.Id is "dungeon" or "temple" ? 48 : f.Id == "shimmer" ? 40 : 24;
        return Rectangle.FromLTRB(Math.Max(8, group[0].x - padding), Math.Max(8, group.Min(c => c.top) - padding),
            Math.Min(w.TilesWide - 8, group[^1].x + padding + 1), Math.Min(w.TilesHigh - 8, group.Max(c => c.bottom) + padding + 1));
    }
    public static List<FoundationPatch> AddFromDonor(World target, string donorFile, Options options, string[] requested, Action<string> log, CancellationToken ct, int offset = 0, List<FoundationPatch>? prior = null)
    {
        var donor = WorldIO.Load(donorFile); var donorInfo = Files.Inspect(donorFile); var patches = new List<FoundationPatch>();
        var available = Scan(donor).Where(s => s.Present && requested.Contains(s.Id)).Select(s => s.Id).ToHashSet();
        var planned = new List<(string[] ids, Rectangle box)>();
        // Jungle + temple travel as one real native landmass when both are missing.
        if (available.Contains("jungle") && available.Contains("temple"))
        {
            planned.Add((["jungle", "temple"], Rectangle.Union(Cluster(donor, FoundationCatalog.All.First(f => f.Id == "jungle")), Cluster(donor, FoundationCatalog.All.First(f => f.Id == "temple")))));
            available.Remove("jungle"); available.Remove("temple");
        }
        foreach (var f in FoundationCatalog.All.Where(f => available.Contains(f.Id))) planned.Add(([f.Id], Cluster(donor, f)));
        // Do not duplicate a native chest/altar through intersecting donor crops.
        for (int i = 0; i < planned.Count; i++) for (int j = i + 1; j < planned.Count; j++)
            if (planned[i].box.IntersectsWith(planned[j].box))
            {
                planned[i] = (planned[i].ids.Concat(planned[j].ids).Distinct().ToArray(), Rectangle.Union(planned[i].box, planned[j].box));
                planned.RemoveAt(j); i = -1; break;
            }
        foreach (var (ids, rawBox) in planned.OrderByDescending(p => p.box.Width * p.box.Height))
        {
            ct.ThrowIfCancellationRequested();
            Rectangle box = ExpandObjects(donor, rawBox);
            Rectangle destination = FindPlacement(target, donor, box, options, [..prior ?? [], ..patches], offset);
            log("补入原生环境 " + string.Join("/", ids) + $"：{box.Width}×{box.Height} → 母图 {destination.X},{destination.Y}；不改接缝/中央出生点。");
            patches.Add(Transplant(target, donor, donorInfo, ids, box, destination));
        }
        donor.Tiles = null!; WorldIO.Collect(); return patches;
    }
    static Rectangle ExpandObjects(World donor, Rectangle box)
    {
        // Chests and signs straddling a crop edge must be included in full.
        bool changed;
        do
        {
            changed = false;
            foreach (var c in donor.Chests.Select(c => new Rectangle(c.X, c.Y, 2, 2)).Concat(donor.Signs.Select(s => new Rectangle(s.X, s.Y, 2, 2))))
                if (box.IntersectsWith(c) && !box.Contains(c)) { box = Rectangle.Union(box, c); changed = true; }
        } while (changed);
        return box;
    }
    internal static Rectangle FindPlacement(World target, World donor, Rectangle box, Options o, List<FoundationPatch> prior, int offset = 0)
    {
        // Fixed layer-preserving Y; never move a temple into the surface to fit.
        int y = Math.Clamp(box.Y + (int)Math.Round(target.GroundLevel - donor.GroundLevel), 16, target.TilesHigh - box.Height - 16);
        // Deterministic bounded packing in the three slice interiors. Reserve ALL
        // depths of seam strips and each world's central spawn column +/-64 tiles.
        int preferred = prior.Count % 3;
        foreach (int role in new[] { preferred, (preferred + 1) % 3, (preferred + 2) % 3 })
        {
            int origin = offset + role * o.Stride;
            foreach (var segment in new[] { (origin + 96, origin + o.Width / 2 - 72), (origin + o.Width / 2 + 72, origin + o.Width - 96) })
            for (int x = segment.Item1; x + box.Width <= segment.Item2; x += 24)
            {
                var candidate = new Rectangle(x, y, box.Width, box.Height);
                var margin = candidate; margin.Inflate(12, 12);
                if (prior.Any(p => margin.IntersectsWith(p.Destination))) continue;
                if (HasObjects(target, margin)) continue;
                bool empty = true;
                for (int xx = candidate.Left; xx < candidate.Right && empty; xx++) for (int yy = candidate.Top; yy < candidate.Bottom; yy++)
                {
                    var tile = target.Tiles[xx, yy];
                    // Non-empty target regions are never silently bulldozed, even
                    // in custom mode. Find a real empty island slot or fail closed.
                    if (tile.IsActive || tile.Wall != 0 || tile.LiquidAmount != 0) { empty = false; break; }
                }
                if (empty) return candidate;
            }
        }
        throw new InvalidDataException($"找不到可安全放置 {box.Width}×{box.Height} 原生环境的空域；未覆盖原有地形/对象/接缝/中央出生点。请增大尺寸或减少补全项。");
    }
    static bool HasObjects(World w, Rectangle r) => w.Chests.Any(c => r.IntersectsWith(new(c.X, c.Y, 2, 2))) || w.Signs.Any(s => r.IntersectsWith(new(s.X, s.Y, 2, 2))) ||
        w.TileEntities.Any(e => r.Contains(e.PosX, e.PosY)) || w.NPCs.Concat(w.Mobs).Any(n => r.Contains((int)n.Position.X / 16, (int)n.Position.Y / 16));
    internal static FoundationPatch Transplant(World target, World donor, Artifact info, string[] ids, Rectangle source, Rectangle destination)
    {
        if (source.Size != destination.Size || !new Rectangle(0, 0, donor.TilesWide, donor.TilesHigh).Contains(source) ||
            !new Rectangle(0, 0, target.TilesWide, target.TilesHigh).Contains(destination)) throw new InvalidDataException("环境补片越界。");
        if (HasObjects(target, destination)) throw new InvalidDataException("环境补片不得覆盖已有对象。");
        int dx = destination.X - source.X, dy = destination.Y - source.Y;
        string digest = RegionDigest(donor, source);
        for (int x = 0; x < source.Width; x++) for (int y = 0; y < source.Height; y++) target.Tiles[destination.X + x, destination.Y + y] = donor.Tiles[source.X + x, source.Y + y];
        int nc = 0, ns = 0, ne = 0, nn = 0;
        foreach (var c in donor.Chests.Where(c => source.Contains(new Rectangle(c.X, c.Y, 2, 2))).ToArray()) { donor.Chests.Remove(c); c.X += dx; c.Y += dy; target.Chests.Add(c); nc++; }
        foreach (var s in donor.Signs.Where(s => source.Contains(new Rectangle(s.X, s.Y, 2, 2))).ToArray()) { donor.Signs.Remove(s); s.X += dx; s.Y += dy; target.Signs.Add(s); ns++; }
        foreach (var e in donor.TileEntities.Where(e => source.Contains(e.PosX, e.PosY)).ToArray()) { donor.TileEntities.Remove(e); e.PosX = checked((short)(e.PosX + dx)); e.PosY = checked((short)(e.PosY + dy)); target.TileEntities.Add(e); ne++; }
        foreach (var p in donor.PressurePlates.Where(p => source.Contains(p.PosX, p.PosY)).ToArray()) { donor.PressurePlates.Remove(p); p.PosX += dx; p.PosY += dy; target.PressurePlates.Add(p); }
        foreach (var n in donor.NPCs.Where(n => source.Contains((int)n.Position.X / 16, (int)n.Position.Y / 16)).ToArray())
        {
            if (target.NPCs.Any(p => p.SpriteId == n.SpriteId)) continue;
            donor.NPCs.Remove(n); n.Position.X += dx * 16; n.Position.Y += dy * 16;
            n.Home.X += dx; n.Home.Y += dy; target.NPCs.Add(n); nn++;
        }
        if (ids.Contains("dungeon")) { target.DungeonX = donor.DungeonX + dx; target.DungeonY = donor.DungeonY + dy; }
        if (RegionDigest(target, destination) != digest) throw new InvalidDataException("补片与原生素材逐字段不一致。");
        return new(ids, info, source, destination, nc, ns, ne, nn, digest, "native-full-rectangle-with-walls-liquids-frames-loot; before-slicing; no-progression-unlock; no-seam-or-spawn-change");
    }
    internal static string RegionDigest(World w, Rectangle r)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        using var cs = new System.Security.Cryptography.CryptoStream(Stream.Null, hash, System.Security.Cryptography.CryptoStreamMode.Write);
        using (var writer = new BinaryWriter(cs, System.Text.Encoding.UTF8, true))
            for (int x = r.Left; x < r.Right; x++) for (int y = r.Top; y < r.Bottom; y++) WorldIO.WriteTile(writer, w.Tiles[x, y]);
        cs.FlushFinalBlock(); return Convert.ToHexString(hash.Hash!);
    }
}
