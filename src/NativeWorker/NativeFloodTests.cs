using System.Text.Json;
using Terraria;
using UnifierTSL.Servers;

internal static class NativeFloodTests
{
    public static int Run(ServerContext w)
    {
        var results = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Func<bool> test)
        {
            try { if (!test()) throw new Exception("assertion false"); results.Add(new { name, pass = true }); passed++; }
            catch (Exception ex) { results.Add(new { name, pass = false, error = ex.ToString() }); failed++; }
        }
        var c = w.WorldGen;
        void Reset() { c.numTileCount = c.shroomCount = c.lavaCount = c.iceCount = c.sandCount = c.rockCount = 0; c.CountedTiles.Clear(); }
        string State() => JsonSerializer.Serialize(new { c.numTileCount, c.shroomCount, c.lavaCount, c.iceCount, c.sandCount, c.rockCount,
            order = c.CountedTiles.Keys.Select(p => new { p.X, p.Y }) });
        foreach (int pattern in Enumerable.Range(0, 10)) foreach (bool jungle in new[] { false, true }) foreach (bool lava in new[] { false, true })
            Check($"native_recursive_vs_iterative_pattern{pattern}_jungle{jungle}_lava{lava}", () =>
            {
                w.Main.maxTilesX = 40; w.Main.maxTilesY = 40; w.Main.tile = new TileCollection.DefaultTileCollection(40, 40);
                w.Main.tileSolid[1] = true; w.Main.tileSolid[147] = true; w.Main.tileSolid[53] = true;
                for (int x = 0; x < 40; x++) for (int y = 0; y < 40; y++)
                {
                    var t = w.Main.tile[x, y]; bool solid = x < 8 || x > 23 || y < 8 || y > 23 || (x * 7 + y * 11 + pattern) % 7 == 0;
                    t.active(solid); t.type = (ushort)(new[] { 1, 70, 147, 53, 161, 396 }[(x + y + pattern) % 6]);
                    if (x == 12 && y == 13) { t.wall = (ushort)(pattern % 3 == 0 ? 244 : pattern % 3 == 1 ? 2 : 0); }
                    if (x == 14 && y == 15) { t.liquid = 123; t.lava(pattern % 2 == 0); t.shimmer(pattern % 2 != 0); }
                    w.Main.tile[x, y] = t;
                }
                c.maxTileCount = 200; Reset(); c.mfwh_nextCount(15, 15, jungle, lava); string old = State();
                Reset(); NativeFloodCount.Count(c, 15, 15, jungle, lava); return old == State();
            });
        Check("100k_empty_tiles_no_recursive_stack_and_exact_limit", () =>
        {
            w.Main.maxTilesX = 600; w.Main.maxTilesY = 600; w.Main.tile = new TileCollection.DefaultTileCollection(600, 600);
            for (int x = 0; x < 600; x++) for (int y = 0; y < 600; y++) if (x < 10 || x > 589 || y < 10 || y > 589) { var t = w.Main.tile[x, y]; t.active(true); t.type = 1; w.Main.tile[x, y] = t; }
            c.maxTileCount = 100000; Reset(); NativeFloodCount.Count(c, 300, 300, false, false);
            if (c.numTileCount != 100000 || c.CountedTiles.Count != 100000) throw new Exception($"num={c.numTileCount},keys={c.CountedTiles.Count},max={c.maxTileCount},borderSolid={c.SolidTile(5,5)},centerSolid={c.SolidTile(300,300)},wall={w.Main.tile[300,300].wall}");
            return true;
        });
        Check("world_border_earlyout_preserved", () => { Reset(); NativeFloodCount.Count(c, 1, 30, false, false); return c.numTileCount == c.maxTileCount && c.CountedTiles.Count == 0; });
        File.WriteAllText("flood-tests.json", JsonSerializer.Serialize(new { passed, failed, results, worker = typeof(NativeFloodTests).Assembly.Location,
            networkStarted = false, scope = "actual pinned native recursive body vs iterative DFS order/counters on bounded fixtures; deep fixture iterative-only" }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }
}
