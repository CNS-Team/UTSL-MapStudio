using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using TEdit.Geometry;
using TEdit.Terraria;

namespace ThreeWorlds;

internal static class WorldIO
{
    static bool initialized;
    public static void Initialize()
    {
        if (initialized) return;
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().BuildApp();
        TerrariaDataStore.Initialize(); WorldConfiguration.Initialize(); initialized = true;
    }
    public static World Load(string path)
    {
        Initialize();
        var (world, error) = World.LoadWorld(path, false, null);
        if (error != null || world?.Tiles == null) throw new InvalidDataException("地图读取失败：" + path, error);
        if (world.IsTModLoader) throw new InvalidDataException("本工具仅接受原版/UTSL .wld，不接受有 .twld 伴随数据的模组地图。");
        return world;
    }
    public static void Save(World world, string path)
    {
        world.PreserveBorderBrickStyles = true;
        World.Save(world, path, resetTime: false, versionOverride: 0, incrementRevision: false, preserveAll: true);
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("地图未成功写入：" + path);
    }
    static bool Solid(Tile tile) => tile.IsActive && !tile.InActive &&
        TerrariaDataStore.Instance.TileById.TryGetValue(tile.Type, out var p) && p.IsSolid && !p.IsPlatform;
    static readonly HashSet<ushort> Geological = [0, 1, 2, 23, 25, 53, 59, 60, 70, 109, 112, 116, 117, 147, 161, 163, 164, 199, 200, 203, 234, 396, 397];

    // worldSurface/GroundLevel is a layer boundary, NOT the height of terrain at X.
    // Scan from the sky down; explicitly skip native cloud-backed floating islands.
    // Never search near the layer line: that can select the floor of an underground cave.
    public static int SurfaceFloor(World w)
    {
        int x = w.TilesWide / 2;
        for (int y = 20; y < w.TilesHigh - 100; y++)
        {
            if (!Solid(w.Tiles[x, y]) || !Geological.Contains(w.Tiles[x, y].Type)) continue;
            // Skip hanging single-tile grass shelves/plant bases; actual terrain has
            // a substantial geological column beneath it, unlike a vine overhang.
            int geologicalSupport = 0;
            for (int dy = 0; dy < 12; dy++)
                if (Solid(w.Tiles[x, y + dy]) && Geological.Contains(w.Tiles[x, y + dy].Type)) geologicalSupport++;
            if (geologicalSupport < 8) continue;
            bool cloudBase = false;
            for (int dx = -16; dx <= 16 && !cloudBase; dx++)
                for (int dy = 1; dy <= 96 && y + dy < w.TilesHigh; dy++)
                {
                    var t = w.Tiles[x + dx, y + dy];
                    if (t.IsActive && t.Type is 189 or 196) { cloudBase = true; break; }
                }
            if (!cloudBase) return y;
        }
        return -1;
    }

    // A fixed central X, real geological surface, three clear tiles above solid ground.
    // No nearest-cave fallback and no modification of the seam strips.
    public static List<TileChange> SetSurfaceSpawn(World w)
    {
        int x = w.TilesWide / 2;
        int naturalFloor = SurfaceFloor(w);
        if (naturalFloor < 0) throw new InvalidDataException($"{w.Title} 中央没有可识别的天然地表，拒绝洞穴/半空兜底。");
        int lo = naturalFloor;
        // A native shallow surface puddle is not a missing surface. Raise a tiny
        // supported pad to its waterline (at most four tiles), rather than drain
        // water out of the player's body or search for a different/cave floor.
        for (int dx = -1; dx <= 1; dx++) for (int yy = naturalFloor - 4; yy < naturalFloor; yy++)
            if (w.Tiles[x + dx, yy].LiquidAmount > 0 && w.Tiles[x + dx, yy].LiquidType == LiquidType.Water)
                lo = Math.Min(lo, yy);
        int hi = lo + 1;
        bool raised = lo < naturalFloor;
        bool CompleteNaturalDecoration(int tx, int ty)
        {
            var t = w.Tiles[tx, ty];
            // Native 3x2 jungle rubble only; all six frame cells must be present
            // inside the audited pad. Do not truncate furniture or registered objects.
            if (t.Type != 186 || t.U < 0 || t.V < 0 || t.U % 18 != 0 || t.V % 18 != 0) return false;
            int left = tx - t.U % 54 / 18, top = ty - t.V % 36 / 18;
            if (left < x - 1 || left + 2 > x + 1 || top < lo - 3 || top + 1 > naturalFloor + 2) return false;
            for (int xx = 0; xx < 3; xx++) for (int yy = 0; yy < 2; yy++)
            {
                var part = w.Tiles[left + xx, top + yy];
                if (!part.IsActive || part.Type != 186 || part.U != t.U - (tx - left) * 18 + xx * 18 || part.V != t.V - (ty - top) * 18 + yy * 18) return false;
            }
            return true;
        }
        for (int y = lo; y < hi; y++)
        {
            if (!raised && (!Solid(w.Tiles[x, y]) || !Geological.Contains(w.Tiles[x, y].Type))) continue;
            // The center column must already be surface, not a cave chosen by proximity.
            if (Enumerable.Range(1, 3).Any(dy => Solid(w.Tiles[x, y - dy]))) continue;
            bool good = true;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= naturalFloor - y + 2; dy++)
                {
                    var t = w.Tiles[x + dx, y + dy];
                    if ((t.LiquidAmount > 0 && t.LiquidType != LiquidType.Water) || t.IsChest() || t.IsSign() || t.IsTileEntity()) good = false;
                    if (raised && t.IsActive && !Geological.Contains(t.Type) && !CompleteNaturalDecoration(x + dx, y + dy)) good = false;
                }
                for (int dy = -3; dy <= -1; dy++)
                {
                    var t = w.Tiles[x + dx, y + dy];
                    if ((Solid(t) && !Geological.Contains(t.Type)) || t.LiquidAmount > 0 || t.IsChest() || t.IsSign() || t.IsTileEntity()) good = false;
                }
            }
            if (raised)
                for (int dx = -3; dx <= 3; dx++) for (int dy = -3; dy < 0; dy++)
                    if (w.Tiles[x + dx, y + dy].LiquidAmount > 0) good = false;
            if (!good) continue;
            var changes = new List<TileChange>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -3; dy <= -1; dy++)
                {
                    ref var t = ref w.Tiles[x + dx, y + dy];
                    if (!t.IsActive) continue;
                    t.IsActive = false; t.Type = 0; t.U = 0; t.V = 0; t.TileColor = 0;
                    t.BrickStyle = BrickStyle.Full; t.Actuator = false; t.InActive = false;
                    changes.Add(new(x + dx, y + dy, "中央地表出生点3×3净空，保留墙/线；不涉及接缝"));
                }
            for (int dx = -1; dx <= 1; dx++) for (int dy = 0; dy <= naturalFloor - y + 2; dy++)
            {
                ref var t = ref w.Tiles[x + dx, y + dy];
                if (Solid(t) && t.BrickStyle == BrickStyle.Full && t.LiquidAmount == 0) continue;
                t.IsActive = true; t.Type = 0; t.U = 0; t.V = 0; t.BrickStyle = BrickStyle.Full;
                t.InActive = false; t.Actuator = false; t.LiquidAmount = 0; t.LiquidType = LiquidType.None;
                changes.Add(new(x + dx, y + dy, raised
                    ? "中央天然地表浅水：3格宽支撑台抬至水面（至多4格），完整小型天然装饰明确替换；不排身体水/不改接缝"
                    : "中央地表3格宽站立面：至多3格厚局部整平/补土/排普通水；不排身体区域液体"));
            }
            w.SpawnX = x; w.SpawnY = y - 1;
            return changes;
        }
        throw new InvalidDataException($"{w.Title} 中央 x={x} 没有安全干燥地表；拒绝退回洞穴/半空。请换种子或处理母图中央地表后重试。");
    }
    public static bool SpawnSafe(World w)
    {
        if (w.SpawnX != w.TilesWide / 2 || w.SpawnY < 3 || w.SpawnY >= w.TilesHigh - 1) return false;
        if (!w.RemixWorld && !w.ZenithWorld && !w.SkyblockWorld && SurfaceFloor(w) != w.SpawnY + 1) return false;
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!Solid(w.Tiles[w.SpawnX + dx, w.SpawnY + 1])) return false;
            for (int dy = -2; dy <= 0; dy++)
                if (Solid(w.Tiles[w.SpawnX + dx, w.SpawnY + dy]) || w.Tiles[w.SpawnX + dx, w.SpawnY + dy].LiquidAmount > 0) return false;
        }
        return true;
    }
    static List<TileChange> SetWorldSpawn(World w, Options options)
    {
        if (!w.SkyblockWorld && !w.RemixWorld && !w.ZenithWorld)
        {
            if (options.Style != "archipelago") return SetSurfaceSpawn(w);
            try { return SetSurfaceSpawn(w); }
            catch (InvalidDataException)
            {
                // Experimental islands may have WATER at the exact center. Keep
                // that fixed X; build a small audited dry starter island at the
                // actual water surface, never use a submerged cave floor.
                int center = w.TilesWide / 2, surface = -1;
                for (int yy = 24; yy < w.TilesHigh - 40; yy++)
                {
                    var tile = w.Tiles[center, yy];
                    if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidType.Water) { surface = yy; break; }
                    if (Solid(tile) && Geological.Contains(tile.Type)) break;
                }
                if (surface < 0) throw new InvalidDataException("群岛中央没有安全地表或可建立起始岛的水面；不改送洞穴。");
                for (int dx = -12; dx <= 12; dx++) for (int dy = -3; dy <= 11; dy++)
                {
                    bool fill = dy >= 0 && Math.Abs(dx) <= 12 - dy;
                    if (!fill && (Math.Abs(dx) > 2 || dy >= 0)) continue;
                    var tile = w.Tiles[center + dx, surface + dy];
                    if (tile.IsActive && !Geological.Contains(tile.Type) && tile.Type is not (184 or 185 or 186 or 187) || tile.IsChest() || tile.IsSign() || tile.IsTileEntity() || tile.LiquidAmount > 0 && tile.LiquidType != LiquidType.Water)
                        throw new InvalidDataException($"群岛中央水面 {center + dx},{surface + dy} 含结构/危险液体(type={tile.Type},active={tile.IsActive},liquid={tile.LiquidType})，不覆盖来建起始岛。");
                }
                var edits = new List<TileChange>();
                for (int dx = -12; dx <= 12; dx++) for (int dy = -3; dy <= 11; dy++)
                {
                    bool fill = dy >= 0 && Math.Abs(dx) <= 12 - dy;
                    if (!fill && (Math.Abs(dx) > 2 || dy >= 0)) continue;
                    w.Tiles[center + dx, surface + dy] = new Tile { IsActive = fill, Type = (ushort)(fill && dy == 0 ? 2 : 0) };
                    edits.Add(new(center + dx, surface + dy, "群岛中央水面干燥起始岛；不改接缝/不加箱子物品"));
                }
                w.SpawnX = center; w.SpawnY = surface - 1; return edits;
            }
        }
        int x = w.TilesWide / 2;
        // Special worlds keep the native spawn LAYER, not the ordinary surface layer.
        int hint = Math.Clamp(w.SpawnY + 1, 30, w.TilesHigh - 30);
        var candidates = Enumerable.Range(Math.Max(8, hint - 100), Math.Min(w.TilesHigh - 8, hint + 101) - Math.Max(8, hint - 100))
            .OrderBy(y => Math.Abs(y - hint));
        foreach (int y in candidates)
        {
            bool safe = true;
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Solid(w.Tiles[x + dx, y])) { safe = false; break; }
                for (int dy = -3; dy < 0; dy++)
                {
                    var t = w.Tiles[x + dx, y + dy];
                    if (Solid(t) || t.LiquidAmount != 0) safe = false;
                }
            }
            if (safe) { w.SpawnX = x; w.SpawnY = y - 1; return []; }
        }
        if (!w.SkyblockWorld) throw new InvalidDataException(w.Title + " 特殊世界中央原生出生层无安全落脚点；不送回普通地表或跨层兜底。请换种子。");
        // An ultra-wide Skyblock may put all its islands in one slice. Add a small,
        // fully audited starter island to empty sky at each slice's central X only.
        foreach (int y in candidates)
        {
            bool empty = true;
            for (int dx = -8; dx <= 8 && empty; dx++) for (int dy = -5; dy <= 4; dy++)
            {
                var t = w.Tiles[x + dx, y + dy];
                if (t.IsActive || t.LiquidAmount > 0 || t.Wall != 0) { empty = false; break; }
            }
            if (!empty) continue;
            var changes = new List<TileChange>();
            for (int dx = -8; dx <= 8; dx++) for (int dy = 0; dy <= 4; dy++)
            {
                if (Math.Abs(dx) + dy * 2 > 8) continue;
                w.Tiles[x + dx, y + dy] = new Tile { IsActive = true, Type = (ushort)(dy == 0 ? 2 : 0) };
                changes.Add(new(x + dx, y + dy, "Skyblock中央小型起始岛；不加物品/箱子，不改接缝"));
            }
            w.SpawnX = x; w.SpawnY = y - 1; return changes;
        }
        throw new InvalidDataException(w.Title + " 空岛中央无安全空域可设起始岛；不覆盖已有对象。");
    }
    public static SliceResult Slice(string source, string sourceHash, SliceSpec spec, Options options, string path)
    {
        var w = Load(source);
        int start = spec.SourceX, width = spec.Width;
        if (start < 0 || start + width > w.TilesWide || spec.Height != w.TilesHigh)
            throw new InvalidDataException("母图尺寸不足，禁止拉伸或拼凑填充。");
        var tiles = new Tile[width, spec.Height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < spec.Height; y++) tiles[x, y] = w.Tiles[start + x, y];
        w.Tiles = tiles; w.TilesWide = width; w.Title = spec.Name;
        w.LeftWorld = 0; w.RightWorld = width * 16; w.TopWorld = 0; w.BottomWorld = spec.Height * 16;
        var id = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceHash}|{spec.Role}|{spec.Name}|{start}|{width}|{spec.Height}"));
        w.WorldGUID = new Guid(id.AsSpan(0, 16)); w.WorldId = BitConverter.ToInt32(id, 16) & int.MaxValue;
        int Shift(int value) => Math.Clamp(value - start, 0, width);
        w.DungeonX = Math.Clamp(w.DungeonX - start, 0, width - 1);
        w.InvasionX = Math.Clamp(w.InvasionX - start, 0, width - 1);
        w.TreeX0 = Shift(w.TreeX0); w.TreeX1 = Shift(w.TreeX1); w.TreeX2 = Shift(w.TreeX2);
        w.CaveBackX0 = Shift(w.CaveBackX0); w.CaveBackX1 = Shift(w.CaveBackX1); w.CaveBackX2 = Shift(w.CaveBackX2);
        int cleared = 0;
        var exclusions = new List<string>();
        foreach (var c in w.Chests.ToArray())
        {
            int gx = c.X; c.X -= start;
            // Partially cut objects can occur only inside the inaccessible 40-tile exterior buffer.
            if (c.X < 0 || c.X + 2 > width)
            {
                if (c.X >= -2 && c.X < width) exclusions.Add($"chest partial at local {c.X},{c.Y}, global {gx}; inaccessible exterior buffer only; complete copy in adjacent map");
                w.Chests.Remove(c); continue;
            }
            if (options.UniqueOverlapLoot && Owner(gx, options, start - RoleIndex(spec.Role) * options.Stride) != spec.Role)
            {
                foreach (var item in c.Items) item.NetId = 0;
                cleared++;
            }
        }
        foreach (var s in w.Signs.ToArray())
        {
            s.X -= start;
            if (s.X < 0 || s.X + 2 > width)
            {
                if (s.X >= -2 && s.X < width) exclusions.Add($"sign partial at local {s.X},{s.Y}; inaccessible exterior buffer only");
                w.Signs.Remove(s);
            }
        }
        foreach (var e in w.TileEntities.ToArray())
        {
            int x = e.PosX - start;
            if (x < 0 || x >= width) { w.TileEntities.Remove(e); continue; }
            e.PosX = (short)x;
        }
        foreach (var p in w.PressurePlates.ToArray())
        {
            p.PosX -= start;
            if (p.PosX < 0 || p.PosX >= width) w.PressurePlates.Remove(p);
        }
        foreach (var n in w.NPCs.Concat(w.Mobs).ToArray())
        {
            // NPC position is pixels, housing is tiles. Never subtract tile X from pixel X.
            float px = n.Position.X - start * 16;
            if (px < 0 || px >= width * 16) { w.NPCs.Remove(n); w.Mobs.Remove(n); continue; }
            n.Position.X = px;
            int home = n.Home.X - start;
            if (home < 0 || home >= width) { n.IsHomeless = true; n.Home.X = width / 2; }
            else n.Home.X = home;
        }
        foreach (var r in w.PlayerRooms.ToArray())
        {
            int x = r.Home.X - start;
            if (x < 0 || x >= width) w.PlayerRooms.Remove(r);
            else r.Home = new Vector2Int32(x, r.Home.Y);
        }
        var changes = SetWorldSpawn(w, options);
        foreach (var s in w.TeamSpawns)
        {
            int x = s.X - start;
            if (x < 0 || x >= width) { s.X = w.SpawnX; s.Y = w.SpawnY; } else s.X = x;
        }
        Save(w, path);
        string savedMetadata = MetadataDigest(w);
        // Inspect serialized output, not just the in-memory object passed to Save.
        w.Tiles = null!; Collect();
        var loaded = Load(path);
        if (!SpawnSafe(loaded)) throw new InvalidDataException("落盘后出生点不安全。");
        if (MetadataDigest(loaded) != savedMetadata) throw new InvalidDataException("对象元数据未完整往返保存。");
        var original = Load(source);
        var allowed = changes.Select(c => (c.X, c.Y)).ToHashSet();
        long differences = 0;
        for (int x = 0; x < width; x++) for (int y = 0; y < spec.Height; y++)
            if (!loaded.Tiles[x, y].Equals(original.Tiles[start + x, y]) && !allowed.Contains((x, y))) differences++;
        original.Tiles = null!;
        if (differences != 0) throw new InvalidDataException($"{spec.Name} 除出生点明确净空外还有 {differences} 格母图差异，拒绝交付。");
        var result = new SliceResult(spec, Files.Inspect(path), loaded.WorldGUID, loaded.WorldId, loaded.SpawnX, loaded.SpawnY,
            loaded.Chests.Count, loaded.Signs.Count, loaded.TileEntities.Count, loaded.NPCs.Count, loaded.Mobs.Count,
            cleared, changes, Digest(loaded), MetadataDigest(loaded), differences, exclusions);
        loaded.Tiles = null!; Collect(); return result;
    }
    static int RoleIndex(string role) => role == "west" ? 0 : role == "middle" ? 1 : 2;
    static string Owner(int globalX, Options o, int offset) => globalX < offset + o.Width - o.EdgeInset ? "west" :
        globalX < offset + o.Stride + o.Width - o.EdgeInset ? "middle" : "east";
    public static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    // Fixed canonical representation of all serialized tile properties, not renderer caches.
    public static void WriteTile(BinaryWriter b, Tile t)
    {
        b.Write(t.IsActive); b.Write(t.Type); b.Write(t.U); b.Write(t.V); b.Write(t.Wall);
        b.Write(t.TileColor); b.Write(t.WallColor); b.Write(t.LiquidAmount); b.Write((byte)t.LiquidType);
        b.Write(t.WireRed); b.Write(t.WireGreen); b.Write(t.WireBlue); b.Write(t.WireYellow);
        b.Write((byte)t.BrickStyle); b.Write(t.Actuator); b.Write(t.InActive);
        b.Write(t.InvisibleBlock); b.Write(t.InvisibleWall); b.Write(t.FullBrightBlock); b.Write(t.FullBrightWall);
    }
    public static string Digest(World w, int start = 0, int? count = null)
    {
        using var sha = SHA256.Create(); using var cs = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using (var b = new BinaryWriter(new BufferedStream(cs, 1024 * 1024), Encoding.UTF8, true))
        {
            int end = start + (count ?? w.TilesWide);
            for (int x = start; x < end; x++) for (int y = 0; y < w.TilesHigh; y++) WriteTile(b, w.Tiles[x, y]);
        }
        cs.FlushFinalBlock(); return Convert.ToHexString(sha.Hash!);
    }
    public static string MetadataDigest(World w)
    {
        using var ms = new MemoryStream(); using var b = new BinaryWriter(ms, Encoding.UTF8, true);
        b.Write(w.Title); b.Write(w.WorldGUID.ToByteArray()); b.Write(w.WorldId); b.Write(w.SpawnX); b.Write(w.SpawnY);
        b.Write(w.GameMode); b.Write(w.GroundLevel); b.Write(w.RockLevel);
        b.Write(w.SkyblockWorld); b.Write(w.RemixWorld); b.Write(w.ZenithWorld);
        foreach (var c in w.Chests.OrderBy(c => c.X).ThenBy(c => c.Y))
        {
            b.Write(c.X); b.Write(c.Y); b.Write(c.Name ?? "");
            foreach (var i in c.Items) { b.Write(i.NetId); b.Write(i.StackSize); b.Write(i.Prefix); }
        }
        foreach (var s in w.Signs.OrderBy(s => s.X).ThenBy(s => s.Y)) { b.Write(s.X); b.Write(s.Y); b.Write(s.Text ?? ""); }
        foreach (var e in w.TileEntities.OrderBy(e => e.PosX).ThenBy(e => e.PosY)) e.Save(b, w.Version);
        foreach (var n in w.NPCs.Concat(w.Mobs)) { b.Write(n.SpriteId); b.Write(n.Position.X); b.Write(n.Position.Y); b.Write(n.Home.X); b.Write(n.Home.Y); b.Write(n.IsHomeless); }
        b.Flush(); return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }
    public static SeamResult Compare(string left, string right, int overlap)
    {
        var a = Load(left); var b = Load(right);
        if (a.TilesHigh != b.TilesHigh || overlap <= 0 || overlap > Math.Min(a.TilesWide, b.TilesWide)) throw new InvalidDataException("接缝尺寸不匹配。");
        long mismatches = 0;
        for (int x = 0; x < overlap; x++) for (int y = 0; y < a.TilesHigh; y++)
            if (!a.Tiles[a.TilesWide - overlap + x, y].Equals(b.Tiles[x, y])) mismatches++;
        var result = new SeamResult(a.Title, b.Title, (long)overlap * a.TilesHigh, mismatches,
            Digest(a, a.TilesWide - overlap, overlap), Digest(b, 0, overlap));
        a.Tiles = null!; b.Tiles = null!; Collect(); return result;
    }
}
