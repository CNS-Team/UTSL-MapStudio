using TEdit.Terraria;

namespace ThreeWorlds;

public sealed class FoundationOptions
{
    // Ordinary/custom worlds remain native unless the user explicitly opts in.
    // A copied skyblock seed also qualifies, independently of the style selector.
    public string Mode { get; set; } = "skyblock-auto";
    public string[] Features { get; set; } = ["dungeon", "temple", "jungle", "crimson", "corruption", "shimmer"];
    public void Validate()
    {
        if (Mode is not ("disabled" or "skyblock-auto" or "enabled")) throw new ArgumentException("未知基础环境策略。");
        if (Features == null || Features.Distinct().Count() != Features.Length || Features.Any(f => !FoundationCatalog.All.Any(v => v.Id == f)))
            throw new ArgumentException("基础环境列表含未知或重复项。");
    }
    public bool Applies(World w) => Mode == "enabled" || Mode == "skyblock-auto" && w.SkyblockWorld;
}

public sealed record FoundationFeature(string Id, string Name, long Minimum, ushort[] TileTypes);
internal static class FoundationCatalog
{
    public static readonly FoundationFeature[] All = [
        new("dungeon", "地牢", 500, [41, 43, 44]),
        new("temple", "神庙", 200, [226]),
        new("jungle", "丛林", 500, [60]),
        new("crimson", "猩红", 400, [199, 200, 203, 234]),
        new("corruption", "腐化", 400, [23, 25, 112, 163]),
        new("shimmer", "微光", 100, []),
        new("snow", "雪原", 1500, [147, 161]),
        new("desert", "沙漠", 1500, [53, 396, 397]),
        new("mushroom", "发光蘑菇", 150, [70])
    ];
    public static bool Matches(Tile t, FoundationFeature f) => f.Id == "shimmer" ? t.LiquidType == LiquidType.Shimmer && t.LiquidAmount > 0 : t.IsActive && f.TileTypes.Contains(t.Type);
}
