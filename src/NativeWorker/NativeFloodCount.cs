using Microsoft.Xna.Framework;
using Terraria;

// Translation of this pinned engine's mfwh_nextCount, with recursion replaced
// by a reverse-pushed stack. DFS visitation/counter order and early-outs match.
// Not a generic flood-fill and not installed into any live server.
internal static class NativeFloodCount
{
    public static void Count(WorldGenSystemContext ctx, int startX, int startY, bool jungle, bool lavaOk)
    {
        var root = ctx.root;
        var pending = new Stack<Point>(); pending.Push(new(startX, startY));
        while (pending.TryPop(out var p))
        {
            int x = p.X, y = p.Y;
            if (ctx.numTileCount >= ctx.maxTileCount) { pending.Clear(); break; }
            if (x <= 1 || x >= root.Main.maxTilesX - 1 || y <= 1 || y >= root.Main.maxTilesY - 1)
            { ctx.numTileCount = ctx.maxTileCount; continue; }
            if (ctx.CountedTiles.ContainsKey(p)) continue;
            var t = root.Main.tile[x, y];
            if (t.wall == 244 || (t.shimmer() && t.liquid > 0)) { ctx.numTileCount = ctx.maxTileCount; continue; }
            if (!jungle)
            {
                if (t.wall != 0) { ctx.numTileCount = ctx.maxTileCount; continue; }
                if (t.lava() && t.liquid > 0)
                {
                    ctx.lavaCount++;
                    if (!lavaOk) { ctx.numTileCount = ctx.maxTileCount; continue; }
                }
            }
            if (t.active())
            {
                if (t.type == 70) ctx.shroomCount++;
                if (t.type == 1) ctx.rockCount++;
                if (t.type is 147 or 161) ctx.iceCount++;
                if (t.type is 53 or 396 or 397) ctx.sandCount++;
            }
            if (ctx.SolidTile(x, y)) continue;
            ctx.CountedTiles.Add(p, true); ctx.numTileCount++;
            pending.Push(new(x, y + 1)); pending.Push(new(x, y - 1));
            pending.Push(new(x + 1, y)); pending.Push(new(x - 1, y));
        }
    }
}
