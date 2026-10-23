using System.Drawing;
using TEdit.Terraria;

namespace ThreeWorlds;

public sealed record ChestRelocation(int FromX, int FromY, int ToX, int ToY, string LootDigest, string TileDigest);
public sealed record FrameRelocation(ushort Type, Rectangle From, Rectangle To, string TileDigest, ChestRelocation[] ChestMoves);
public sealed record NormalCoastAudit(string Role, int SeaY, double GroundLevel, double RockLevel, long ChangedTiles, string InlandBefore, string InlandAfter, string PreparedFile, ChestRelocation[] ChestMoves, FrameRelocation[] ObjectMoves);
internal static class NormalWorlds
{
    static readonly HashSet<ushort> Plants = [3, 5, 24, 32, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 165, 201, 205, 227, 233, 323];
    internal static int FindSea(World w, int x)
    {
        for (int y = 30; y < w.TilesHigh / 2; y++)
            if (!w.Tiles[x, y].IsActive && w.Tiles[x, y].LiquidType == LiquidType.Water && w.Tiles[x, y].LiquidAmount >= 128 &&
                Enumerable.Range(y, 8).All(yy => !w.Tiles[x, yy].IsActive && w.Tiles[x, yy].LiquidAmount > 0 && w.Tiles[x, yy].LiquidType == LiquidType.Water)) return y;
        throw new InvalidDataException($"{w.Title} x={x} 无可验证普通海面；特殊/空岛/倒置世界请选连续母图模式。");
    }
    static void GuardBand(World w, int left, int width)
    {
        var area = new Rectangle(left, 0, width, w.TilesHigh);
        if (w.Chests.Any(c => area.IntersectsWith(new(c.X, c.Y, 2, 2))) || w.Signs.Any(c => area.IntersectsWith(new(c.X, c.Y, 2, 2))) ||
            w.TileEntities.Any(c => area.Contains(c.PosX, c.PosY)) || w.PressurePlates.Any(c => area.Contains(c.PosX, c.PosY)))
            throw new InvalidDataException(w.Title + " 海边80格包含箱子/牌子/实体，拒绝删除或复制对象；请更换种子。");
        for (int x = left; x < left + width; x++) for (int y = 0; y < w.TilesHigh; y++)
        {
            var t = w.Tiles[x, y];
            if (t.IsActive && !Plants.Contains(t.Type) && t.Type < w.TileFrameImportant.Length && w.TileFrameImportant[t.Type])
                throw new InvalidDataException($"{w.Title} 海边接缝有原生框架对象 {t.Type}@{x},{y}；拒绝破坏对象，请更换种子。");
        }
    }
    static Tile Natural(Tile t, World w)
    {
        // Never extrude a framed chest, door, tree or other multi-tile object.
        if (t.IsActive && (t.IsChest() || t.IsSign() || t.IsTileEntity() || t.Type < w.TileFrameImportant.Length && w.TileFrameImportant[t.Type]))
        { t.IsActive = false; t.Type = 0; t.U = t.V = 0; }
        if (t.IsActive) t.BrickStyle = BrickStyle.Full;
        t.WireRed = t.WireGreen = t.WireBlue = t.WireYellow = t.Actuator = t.InActive = false;
        return t;
    }
    static string Loot(Chest c) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        c.Name + "|" + string.Join(";", c.Items.Select(i => $"{i.NetId}:{i.StackSize}:{i.Prefix}")))));
    static FrameRelocation[] ProtectFrameObjects(World w, int overlap)
    {
        var result = new List<FrameRelocation>();
        bool Framed(Tile t) => t.IsActive && !Plants.Contains(t.Type) && t.Type < w.TileFrameImportant.Length && w.TileFrameImportant[t.Type];
        foreach (bool east in new[] { false, true })
        for (int column = 0; column < overlap; column++) for (int y = 1; y < w.TilesHigh - 3; y++)
        {
            int sx = east ? w.TilesWide - 1 - column : column;
            var t = w.Tiles[sx, y]; if (!Framed(t)) continue;
            var cells = new HashSet<Point>(); var pending = new Queue<Point>(); pending.Enqueue(new(sx, y));
            int left, top, width, height;
            while (true)
            {
                while (pending.TryDequeue(out var p))
                {
                    if (p.X < 0 || p.Y < 0 || p.X >= w.TilesWide || p.Y >= w.TilesHigh || !Framed(w.Tiles[p.X, p.Y]) || !cells.Add(p)) continue;
                    if (cells.Count > 144) throw new InvalidDataException("接缝有大型原生框架结构，拒绝拆散/删除。");
                    pending.Enqueue(new(p.X - 1, p.Y)); pending.Enqueue(new(p.X + 1, p.Y)); pending.Enqueue(new(p.X, p.Y - 1)); pending.Enqueue(new(p.X, p.Y + 1));
                }
                left = cells.Min(p => p.X); top = cells.Min(p => p.Y); width = cells.Max(p => p.X) - left + 1; height = cells.Max(p => p.Y) - top + 2;
                if (width > 96 || height > 14 || top + height >= w.TilesHigh) throw new InvalidDataException($"接缝组合结构过大：type={t.Type},x={left},y={top},width={width},height={height}。");
                // Include every framed object inside the copy rectangle/support
                // row as a complete group, rather than copying an untracked half.
                for (int xx = left; xx < left + width; xx++) for (int yy = top; yy < top + height; yy++)
                    if (Framed(w.Tiles[xx, yy]) && !cells.Contains(new(xx, yy))) pending.Enqueue(new(xx, yy));
                if (pending.Count == 0) break;
            }
            // A connected native minecart track is wider than furniture, but must
            // still move as one intact row, never split/delete a track segment.
            int maxWidth = cells.Any(p => w.Tiles[p.X, p.Y].Type == 314) ? 96 : 12;
            if (width > maxWidth || height > 14 || top + height >= w.TilesHigh || left < 0 || left + width > w.TilesWide ||
                !(left + width <= 220 || left >= w.TilesWide - 220)) throw new InvalidDataException($"接缝结构不能安全整体保护：type={t.Type},x={left},y={top},width={width},height={height},cells={cells.Count},world={w.Title}。");
            var from = new Rectangle(left, top, width, height);
            for (int xx = from.Left; xx < from.Right; xx++) for (int yy = from.Top; yy < from.Bottom; yy++)
                if (Framed(w.Tiles[xx, yy]) && !cells.Contains(new(xx, yy))) throw new InvalidDataException($"接缝结构包围盒包含另一框架对象：type={t.Type},other={w.Tiles[xx, yy].Type}@{xx},{yy}，拒绝复制/拆分。");
            var chests = w.Chests.Where(c => from.IntersectsWith(new(c.X, c.Y, 2, 2))).ToArray();
            if (chests.Any(c => !from.Contains(new Rectangle(c.X,c.Y,2,3)) || w.Tiles[c.X,c.Y].Type is not (21 or 467)) ||
                w.Signs.Any(c => from.IntersectsWith(new(c.X,c.Y,2,2))) || w.TileEntities.Any(c => from.Contains(c.PosX,c.PosY)) || w.PressurePlates.Any(c => from.Contains(c.PosX,c.PosY)))
                throw new InvalidDataException($"接缝有不支持整体移动的注册结构：type={t.Type},from={from}，拒绝破坏。");
            string digest = FoundationWorlds.RegionDigest(w, from); bool moved = false;
            for (int d = overlap + 20; d + width < 218 && !moved; d++)
            {
                int x = east ? w.TilesWide - d - width : d; var to = new Rectangle(x, top, width, height); var avoid = Rectangle.Inflate(to, 1, 1);
                if (result.Any(m => avoid.IntersectsWith(m.To))) continue;
                if (w.Chests.Any(c => avoid.IntersectsWith(new(c.X,c.Y,2,2))) || w.Signs.Any(c => avoid.IntersectsWith(new(c.X,c.Y,2,2))) || w.TileEntities.Any(c => avoid.Contains(c.PosX,c.PosY))) continue;
                bool safe = true;
                for (int xx = avoid.Left; xx < avoid.Right && safe; xx++) for (int yy = avoid.Top; yy < avoid.Bottom; yy++)
                { if (yy < 0 || yy >= w.TilesHigh) { safe = false; break; } var q = w.Tiles[xx,yy]; if (Framed(q) || q.WireRed || q.WireGreen || q.WireBlue || q.WireYellow || q.Actuator) { safe = false; break; } }
                if (!safe) continue;
                for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++) w.Tiles[x + dx, top + dy] = w.Tiles[left + dx, top + dy];
                if (FoundationWorlds.RegionDigest(w, to) != digest) throw new InvalidDataException("原生框架对象保护摘要不符。");
                var chestMoves = new List<ChestRelocation>();
                foreach (var c in chests) {
                    int ox = c.X, oy = c.Y; string loot = Loot(c); c.X += x - left;
                    chestMoves.Add(new(ox,oy,c.X,c.Y,loot,FoundationWorlds.RegionDigest(w,new(c.X,c.Y,2,3))));
                }
                foreach (var p in cells) w.Tiles[p.X, p.Y] = new Tile();
                result.Add(new(t.Type, from, to, digest, chestMoves.ToArray())); moved = true;
            }
            if (!moved) throw new InvalidDataException("原生框架对象同Y近海找不到保护位置；拒绝删除。");
        }
        return result.ToArray();
    }
    internal static NormalCoastAudit[] Prepare(string[] inputs, Options o, string output, CancellationToken ct)
    {
        if (inputs.Length != 3) throw new InvalidDataException("必须有三个独立原生地图。");
        var middle = WorldIO.Load(inputs[1]);
        if (middle.SkyblockWorld || middle.RemixWorld || middle.ZenithWorld) throw new InvalidDataException("独立标准模式不支持空岛/颠倒海平面；请使用连续母图模式。");
        int seaY = FindSea(middle, o.EdgeInset); double ground = middle.GroundLevel, rock = middle.RockLevel;
        var profile = Enumerable.Range(0, o.Height).Select(y => Natural(middle.Tiles[o.EdgeInset, y], middle)).ToArray();
        middle.Tiles = null!; WorldIO.Collect();
        var audits = new List<NormalCoastAudit>();
        for (int i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested(); var w = WorldIO.Load(inputs[i]);
            if (w.TilesWide != o.Width || w.TilesHigh != o.Height || w.GameMode != o.Difficulty || w.SkyblockWorld || w.RemixWorld || w.ZenithWorld)
                throw new InvalidDataException("独立地图尺寸/难度/原版地形不符合要求。");
            string before = WorldIO.Digest(w, 220, o.Width - 440);
            var objectMoves = ProtectFrameObjects(w, o.Overlap);
            var chestMoves = objectMoves.SelectMany(m => m.ChestMoves).ToArray();
            GuardBand(w, 0, o.Overlap); GuardBand(w, o.Width - o.Overlap, o.Overlap);
            long changed = 0;
            for (int x = 0; x < o.Width; x++)
            {
                if (x >= o.Overlap && x < o.Width - o.Overlap) continue;
                for (int y = 0; y < o.Height; y++) if (!w.Tiles[x, y].Equals(profile[y])) { w.Tiles[x, y] = profile[y]; changed++; }
            }
            // Normalize the connected ocean WATER surface, not just GroundLevel metadata.
            // Inland terrain, caves, dungeon, temple, NPCs, chests and loot remain in place.
            foreach (bool east in new[] { false, true }) for (int distance = o.Overlap; distance < 220; distance++)
            {
                int x = east ? o.Width - 1 - distance : distance; int oldSea;
                try { oldSea = FindSea(w, x); } catch (InvalidDataException) { break; }
                int bottom = oldSea; while (bottom + 1 < o.Height / 2 && !w.Tiles[x, bottom + 1].IsActive && w.Tiles[x, bottom + 1].LiquidAmount > 0 && w.Tiles[x, bottom + 1].LiquidType == LiquidType.Water) bottom++;
                if (bottom <= seaY) break;
                for (int y = Math.Min(oldSea, seaY); y <= bottom; y++)
                {
                    if (objectMoves.Any(m => m.To.Contains(x, y)) || chestMoves.Any(m => new Rectangle(m.ToX, m.ToY, 2, 3).Contains(x, y))) continue;
                    var t = w.Tiles[x, y]; if (t.IsActive || t.LiquidAmount > 0 && t.LiquidType != LiquidType.Water) continue;
                    byte amount = y < seaY ? (byte)0 : (byte)255;
                    if (t.LiquidAmount != amount) { t.LiquidAmount = amount; t.LiquidType = LiquidType.Water; w.Tiles[x, y] = t; changed++; }
                }
            }
            w.GroundLevel = ground; w.RockLevel = rock;
            string after = WorldIO.Digest(w, 220, o.Width - 440);
            if (before != after) throw new InvalidDataException("标准模式意外改动内陆，拒绝输出。");
            foreach (int x in new[] { o.EdgeInset, o.Width - o.EdgeInset - 1 }) if (FindSea(w, x) != seaY) throw new InvalidDataException("物理海面Y未对齐。");
            foreach (var move in objectMoves) if (FoundationWorlds.RegionDigest(w, move.To) != move.TileDigest) throw new InvalidDataException($"保护对象在保存前改变 type={move.Type},from={move.From},to={move.To}");
            string path = Path.Combine(output, "prepared-" + o.Specs()[i].Role + ".wld"); WorldIO.Save(w, path);
            foreach (var move in chestMoves) if (Loot(w.Chests.Single(c => c.X == move.ToX && c.Y == move.ToY)) != move.LootDigest) throw new InvalidDataException("保护宝箱最终验收失败。");
            foreach (var move in objectMoves) if (FoundationWorlds.RegionDigest(w, move.To) != move.TileDigest) throw new InvalidDataException($"保护对象在保存后改变 type={move.Type},from={move.From},to={move.To}");
            int chestCount = w.Chests.Count; w.Tiles = null!; WorldIO.Collect();
            var reloaded = WorldIO.Load(path);
            if (reloaded.Chests.Count != chestCount) throw new InvalidDataException("原生宝箱数量在保存重读后改变。");
            foreach (var move in chestMoves) if (Loot(reloaded.Chests.Single(c=>c.X==move.ToX&&c.Y==move.ToY)) != move.LootDigest) throw new InvalidDataException("宝箱战利品未完整往返保存。");
            foreach (var move in objectMoves) if (FoundationWorlds.RegionDigest(reloaded, move.To) != move.TileDigest) throw new InvalidDataException("框架对象未完整往返保存。");
            audits.Add(new(o.Specs()[i].Role, seaY, ground, rock, changed, before, after, path, chestMoves, objectMoves)); reloaded.Tiles = null!; WorldIO.Collect();
        }
        return audits.ToArray();
    }
}
